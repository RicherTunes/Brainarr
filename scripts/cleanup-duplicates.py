#!/usr/bin/env python3
"""
Lidarr Duplicate Artist Cleanup Script

This script automatically detects and removes duplicate artists in Lidarr,
specifically targeting the pattern where duplicates have "(2)", "(3)" etc.
suffixes that can be created by import list plugins.

Usage:
    python cleanup-duplicates.py --url http://localhost:8686 --api-key YOUR_API_KEY

Features:
- Detects duplicate artists with numbered suffixes like "Artist (2)"
- Shows preview of what will be removed before taking action
- Handles edge cases safely (won't delete if only one copy exists)
- Provides detailed logging of all actions
- Supports dry-run mode for testing

Author: Brainarr Plugin Team
Version: 1.0.0
"""

import argparse
import json
import logging
import re
import sys
import time
from collections import defaultdict
from typing import Dict, List, Tuple, Optional
from urllib.parse import urljoin

try:
    import requests
except ImportError:
    print("❌ Error: requests library not found. Install with: pip install requests")
    sys.exit(1)


class LidarrAPI:
    """Lidarr API client for managing artists and albums."""
    
    def __init__(self, base_url: str, api_key: str):
        self.base_url = base_url.rstrip('/')
        self.api_key = api_key
        self.session = requests.Session()
        self.session.headers.update({
            'X-Api-Key': api_key,
            'Content-Type': 'application/json'
        })
        
    def _make_request(self, method: str, endpoint: str, **kwargs) -> requests.Response:
        """Make API request with error handling."""
        url = urljoin(f"{self.base_url}/api/v1/", endpoint)
        
        try:
            response = self.session.request(method, url, timeout=30, **kwargs)
            response.raise_for_status()
            return response
        except requests.exceptions.RequestException as e:
            logging.error(f"API request failed: {method} {url} - {e}")
            raise
    
    def get_artists(self) -> List[Dict]:
        """Get all artists from Lidarr."""
        logging.info("📡 Fetching all artists from Lidarr...")
        response = self._make_request('GET', 'artist')
        artists = response.json()
        logging.info(f"✅ Retrieved {len(artists)} artists")
        return artists
    
    def get_artist_albums(self, artist_id: int) -> List[Dict]:
        """Get all albums for a specific artist."""
        response = self._make_request('GET', f'album?artistId={artist_id}')
        return response.json()
    
    def delete_artist(self, artist_id: int, delete_files: bool = False, add_import_exclusion: bool = True) -> bool:
        """Delete an artist from Lidarr."""
        params = {
            'deleteFiles': str(delete_files).lower(),
            'addImportListExclusion': str(add_import_exclusion).lower()
        }
        
        try:
            response = self._make_request('DELETE', f'artist/{artist_id}', params=params)
            return response.status_code == 200
        except Exception as e:
            logging.error(f"Failed to delete artist {artist_id}: {e}")
            return False
    
    def test_connection(self) -> bool:
        """Test connection to Lidarr API."""
        try:
            response = self._make_request('GET', 'system/status')
            status = response.json()
            logging.info(f"✅ Connected to Lidarr {status.get('version', 'unknown')} - {status.get('instanceName', 'Lidarr')}")
            return True
        except Exception as e:
            logging.error(f"❌ Failed to connect to Lidarr: {e}")
            return False


class DuplicateDetector:
    """Detects duplicate artists with various matching strategies."""
    
    # Pattern to match numbered duplicates: "Artist (2)", "Artist (3)", etc.
    DUPLICATE_PATTERN = re.compile(r'^(.+?)\s*\((\d+)\)$')
    
    @staticmethod
    def normalize_name(name: str) -> str:
        """Normalize artist name for matching."""
        if not name:
            return ""
            
        # Convert to lowercase and strip
        normalized = name.lower().strip()
        
        # Remove common punctuation and extra spaces
        normalized = re.sub(r'["\'''""„]', '', normalized)
        normalized = re.sub(r'\s+', ' ', normalized)
        
        # Handle "The" prefix
        if normalized.startswith('the '):
            normalized = normalized[4:]
        
        return normalized
    
    def find_duplicates(self, artists: List[Dict]) -> Dict[str, List[Dict]]:
        """
        Find duplicate artists using multiple strategies.
        
        Returns:
            Dict mapping base names to lists of duplicate artists
        """
        logging.info("🔍 Analyzing artists for duplicates...")
        
        # Group artists by normalized name
        name_groups = defaultdict(list)
        numbered_duplicates = {}
        
        for artist in artists:
            name = artist.get('artistName', '')
            if not name:
                continue
            
            # Check if this is a numbered duplicate like "Artist (2)"
            match = self.DUPLICATE_PATTERN.match(name)
            if match:
                base_name = match.group(1).strip()
                number = int(match.group(2))
                normalized_base = self.normalize_name(base_name)
                
                if normalized_base not in numbered_duplicates:
                    numbered_duplicates[normalized_base] = {}
                
                numbered_duplicates[normalized_base][number] = {
                    'artist': artist,
                    'original_name': name,
                    'base_name': base_name
                }
            
            # Also group by normalized name for general duplicate detection
            normalized = self.normalize_name(name)
            if normalized:
                name_groups[normalized].append(artist)
        
        # Find duplicates
        duplicates = {}
        
        # Process numbered duplicates
        for normalized_base, numbered_artists in numbered_duplicates.items():
            # Look for the original (non-numbered) artist
            original_candidates = [
                artist for artist in artists 
                if self.normalize_name(artist.get('artistName', '')) == normalized_base
                and not self.DUPLICATE_PATTERN.match(artist.get('artistName', ''))
            ]
            
            if original_candidates:
                # We have both original and numbered duplicates
                base_name = original_candidates[0]['artistName']
                duplicates[base_name] = []
                
                # Add all numbered duplicates to removal list
                for number in sorted(numbered_artists.keys()):
                    duplicates[base_name].append(numbered_artists[number])
                
                logging.info(f"🎯 Found numbered duplicates for '{base_name}': {list(numbered_artists.keys())}")
        
        # Also check for exact name duplicates (different from numbered ones)
        for normalized, group in name_groups.items():
            if len(group) > 1:
                # Check if this isn't already handled by numbered duplicates
                first_name = group[0].get('artistName', '')
                if normalized not in duplicates and not any(
                    self.normalize_name(dup_group[0]['artist']['artistName']) == normalized 
                    for dup_group in duplicates.values() 
                    for dup_artist in dup_group
                ):
                    # Keep the first one, mark others as duplicates
                    base_name = first_name
                    duplicates[base_name] = []
                    for artist in group[1:]:
                        duplicates[base_name].append({
                            'artist': artist,
                            'original_name': artist.get('artistName', ''),
                            'base_name': base_name
                        })
                    logging.info(f"🎯 Found exact duplicates for '{base_name}': {len(group)-1} copies")
        
        return duplicates


def format_artist_info(artist: Dict) -> str:
    """Format artist information for display."""
    name = artist.get('artistName', 'Unknown')
    albums = len(artist.get('albums', []))
    monitored = "👁️" if artist.get('monitored', False) else "👁️‍🗨️"
    
    return f"{name} {monitored} ({albums} albums, ID: {artist.get('id', 'unknown')})"


def display_duplicates_summary(duplicates: Dict[str, List[Dict]]) -> None:
    """Display a summary of found duplicates."""
    if not duplicates:
        print("✅ No duplicates found!")
        return
    
    total_to_remove = sum(len(dupe_list) for dupe_list in duplicates.values())
    
    print(f"\n📊 DUPLICATE ANALYSIS RESULTS")
    print(f"{'='*50}")
    print(f"🎯 Found {len(duplicates)} base artists with duplicates")
    print(f"🗑️  Total duplicates to remove: {total_to_remove}")
    print(f"💾 Artists that will be kept: {len(duplicates)}")
    
    for base_name, duplicate_list in duplicates.items():
        print(f"\n🎵 {base_name}")
        print(f"   ✅ Will keep original")
        print(f"   🗑️  Will remove {len(duplicate_list)} duplicate(s):")
        
        for i, dup in enumerate(duplicate_list, 1):
            artist = dup['artist']
            albums = len(artist.get('albums', []))
            status = "monitored" if artist.get('monitored', False) else "unmonitored"
            print(f"      {i}. {dup['original_name']} ({albums} albums, {status})")


def cleanup_duplicates(api: LidarrAPI, duplicates: Dict[str, List[Dict]], dry_run: bool = True) -> Tuple[int, int]:
    """
    Remove duplicate artists from Lidarr.
    
    Returns:
        Tuple of (successful_removals, failed_removals)
    """
    if not duplicates:
        logging.info("No duplicates to clean up.")
        return 0, 0
    
    total_to_remove = sum(len(dupe_list) for dupe_list in duplicates.values())
    
    if dry_run:
        logging.info(f"🧪 DRY RUN: Would remove {total_to_remove} duplicate artists")
        return total_to_remove, 0
    
    logging.info(f"🗑️  Starting cleanup of {total_to_remove} duplicate artists...")
    
    successful = 0
    failed = 0
    
    for base_name, duplicate_list in duplicates.items():
        logging.info(f"🎵 Processing duplicates for: {base_name}")
        
        for dup in duplicate_list:
            artist = dup['artist']
            artist_id = artist.get('id')
            artist_name = dup['original_name']
            
            if not artist_id:
                logging.error(f"❌ No ID found for artist: {artist_name}")
                failed += 1
                continue
            
            logging.info(f"🗑️  Removing duplicate: {artist_name} (ID: {artist_id})")
            
            # Delete the duplicate (don't delete files, add to import exclusion)
            if api.delete_artist(artist_id, delete_files=False, add_import_exclusion=True):
                logging.info(f"✅ Successfully removed: {artist_name}")
                successful += 1
            else:
                logging.error(f"❌ Failed to remove: {artist_name}")
                failed += 1
            
            # Small delay to avoid overwhelming the API
            time.sleep(0.5)
    
    logging.info(f"🏁 Cleanup complete: {successful} removed, {failed} failed")
    return successful, failed


def setup_logging(verbose: bool = False) -> None:
    """Setup logging configuration."""
    level = logging.DEBUG if verbose else logging.INFO
    
    # Create formatter
    formatter = logging.Formatter(
        '%(asctime)s [%(levelname)s] %(message)s',
        datefmt='%H:%M:%S'
    )
    
    # Setup console handler
    console_handler = logging.StreamHandler()
    console_handler.setFormatter(formatter)
    
    # Setup file handler
    file_handler = logging.FileHandler('lidarr-cleanup.log')
    file_handler.setFormatter(formatter)
    
    # Configure root logger
    logging.getLogger().setLevel(level)
    logging.getLogger().addHandler(console_handler)
    logging.getLogger().addHandler(file_handler)


def main():
    """Main script entry point."""
    parser = argparse.ArgumentParser(
        description="Clean up duplicate artists in Lidarr",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Dry run (preview only)
  python cleanup-duplicates.py --url http://localhost:8686 --api-key YOUR_KEY --dry-run
  
  # Actually remove duplicates
  python cleanup-duplicates.py --url http://localhost:8686 --api-key YOUR_KEY
  
  # Verbose output with debug info
  python cleanup-duplicates.py --url http://localhost:8686 --api-key YOUR_KEY --verbose
        """
    )
    
    parser.add_argument(
        '--url', 
        required=True, 
        help='Lidarr URL (e.g., http://localhost:8686)'
    )
    
    parser.add_argument(
        '--api-key', 
        required=True, 
        help='Lidarr API key (found in Settings > General)'
    )
    
    parser.add_argument(
        '--dry-run', 
        action='store_true', 
        help='Preview what would be removed without actually doing it'
    )
    
    parser.add_argument(
        '--verbose', 
        action='store_true', 
        help='Enable verbose debug logging'
    )
    
    parser.add_argument(
        '--auto-confirm', 
        action='store_true', 
        help='Skip confirmation prompt (USE WITH CAUTION!)'
    )
    
    args = parser.parse_args()
    
    # Setup logging
    setup_logging(args.verbose)
    
    print("🎵 Lidarr Duplicate Artist Cleanup Script")
    print("=" * 45)
    
    # Initialize API client
    api = LidarrAPI(args.url, args.api_key)
    
    # Test connection
    if not api.test_connection():
        print("❌ Failed to connect to Lidarr. Check your URL and API key.")
        return 1
    
    try:
        # Get all artists
        artists = api.get_artists()
        
        if not artists:
            print("ℹ️  No artists found in Lidarr.")
            return 0
        
        # Detect duplicates
        detector = DuplicateDetector()
        duplicates = detector.find_duplicates(artists)
        
        # Display summary
        display_duplicates_summary(duplicates)
        
        if not duplicates:
            return 0
        
        # Confirmation prompt
        if not args.dry_run and not args.auto_confirm:
            print(f"\n⚠️  WARNING: This will permanently remove duplicate artists from Lidarr!")
            print("📁 Files will NOT be deleted from disk")
            print("🚫 Artists will be added to import exclusion list")
            
            response = input("\n❓ Do you want to proceed? (yes/no): ").strip().lower()
            if response not in ('yes', 'y'):
                print("❌ Operation cancelled by user.")
                return 0
        
        # Perform cleanup
        successful, failed = cleanup_duplicates(api, duplicates, dry_run=args.dry_run)
        
        # Final summary
        if args.dry_run:
            print(f"\n🧪 DRY RUN COMPLETE")
            print(f"💡 Run without --dry-run to actually remove {successful} duplicates")
        else:
            print(f"\n🏁 CLEANUP COMPLETE")
            print(f"✅ Successfully removed: {successful} artists")
            if failed > 0:
                print(f"❌ Failed to remove: {failed} artists")
                print(f"📋 Check lidarr-cleanup.log for details")
        
        return 0 if failed == 0 else 1
        
    except Exception as e:
        logging.error(f"❌ Script failed with error: {e}")
        if args.verbose:
            logging.exception("Full error details:")
        return 1


if __name__ == '__main__':
    sys.exit(main())