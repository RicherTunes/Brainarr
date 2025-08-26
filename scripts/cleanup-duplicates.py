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
import os
import re
import sys
import time
from collections import defaultdict
from typing import Dict, List, Tuple, Optional
from urllib.parse import urljoin

try:
    import requests
except ImportError:
    safe_print("❌ Error: requests library not found. Install with: pip install requests")
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
            response = self.session.request(method, url, timeout=360, **kwargs)
            response.raise_for_status()
            return response
        except requests.exceptions.RequestException as e:
            logging.error(f"API request failed: {method} {url} - {e}")
            raise
    
    def get_artists(self) -> List[Dict]:
        """Get all artists from Lidarr."""
        safe_log("📡 Fetching all artists from Lidarr...")
        response = self._make_request('GET', 'artist')
        artists = response.json()
        safe_log(f"✅ Retrieved {len(artists)} artists")
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
            safe_log(f"✅ Connected to Lidarr {status.get('version', 'unknown')} - {status.get('instanceName', 'Lidarr')}")
            return True
        except Exception as e:
            safe_log(f"❌ Failed to connect to Lidarr: {e}", 'error')
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
        safe_log("🔍 Analyzing artists for duplicates...")
        
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
                
                safe_log(f"🎯 Found numbered duplicates for '{base_name}': {list(numbered_artists.keys())}")
        
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
                    safe_log(f"🎯 Found exact duplicates for '{base_name}': {len(group)-1} copies")
        
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
        safe_print("✅ No duplicates found!")
        return
    
    total_to_remove = sum(len(dupe_list) for dupe_list in duplicates.values())
    
    safe_print(f"\n📊 DUPLICATE ANALYSIS RESULTS")
    print(f"{'='*50}")
    print(f"🎯 Found {len(duplicates)} base artists with duplicates")
    safe_print(f"🗑️  Total duplicates to remove: {total_to_remove}")
    print(f"💾 Artists that will be kept: {len(duplicates)}")
    
    for base_name, duplicate_list in duplicates.items():
        safe_print(f"\n🎵 {base_name}")
        safe_print(f"   ✅ Will keep original")
        safe_print(f"   🗑️  Will remove {len(duplicate_list)} duplicate(s):")
        
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
        safe_log(f"🧪 DRY RUN: Would remove {total_to_remove} duplicate artists")
        return total_to_remove, 0
    
    safe_log(f"🗑️  Starting cleanup of {total_to_remove} duplicate artists...")
    
    successful = 0
    failed = 0
    
    for base_name, duplicate_list in duplicates.items():
        safe_log(f"🎵 Processing duplicates for: {base_name}")
        
        for dup in duplicate_list:
            artist = dup['artist']
            artist_id = artist.get('id')
            artist_name = dup['original_name']
            
            if not artist_id:
                safe_log(f"❌ No ID found for artist: {artist_name}", 'error')
                failed += 1
                continue
            
            safe_log(f"🗑️  Removing duplicate: {artist_name} (ID: {artist_id})")
            
            # Delete the duplicate (don't delete files, add to import exclusion)
            if api.delete_artist(artist_id, delete_files=False, add_import_exclusion=True):
                safe_log(f"✅ Successfully removed: {artist_name}")
                successful += 1
            else:
                safe_log(f"❌ Failed to remove: {artist_name}", 'error')
                failed += 1
            
            # Small delay to avoid overwhelming the API
            time.sleep(0.5)
    
    safe_log(f"🏁 Cleanup complete: {successful} removed, {failed} failed")
    return successful, failed


def load_env_file(env_path: str = None) -> None:
    """Load environment variables from .env file."""
    if env_path is None:
        # Look for .env file in the same directory as this script
        script_dir = os.path.dirname(os.path.abspath(__file__))
        env_path = os.path.join(script_dir, '.env')
    
    if not os.path.exists(env_path):
        return  # No .env file found, skip
    
    try:
        with open(env_path, 'r', encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                # Skip comments and empty lines
                if not line or line.startswith('#'):
                    continue
                
                # Parse key=value pairs
                if '=' in line:
                    key, value = line.split('=', 1)
                    key = key.strip()
                    value = value.strip()
                    
                    # Remove quotes if present
                    if value.startswith('"') and value.endswith('"'):
                        value = value[1:-1]
                    elif value.startswith("'") and value.endswith("'"):
                        value = value[1:-1]
                    
                    # Set environment variable if not already set
                    if key and not os.getenv(key):
                        os.environ[key] = value
                        
    except Exception as e:
        logging.warning(f"Failed to load .env file: {e}")


def safe_print(message: str) -> None:
    """Print message with Windows-compatible encoding."""
    try:
        print(message)
    except UnicodeEncodeError:
        # Fallback: remove emojis and print plain text
        import re
        clean_message = re.sub(r'[^\x00-\x7F]+', '', message).strip()
        clean_message = re.sub(r'\s+', ' ', clean_message)  # Clean up extra spaces
        if clean_message:
            print(clean_message)


def safe_log(message: str, level: str = 'info') -> None:
    """Log message with Windows-compatible encoding."""
    try:
        # Try to log with emojis
        getattr(logging, level)(message)
    except UnicodeEncodeError:
        # Fallback: remove emojis and log plain text
        import re
        clean_message = re.sub(r'[^\x00-\x7F]+', '', message).strip()
        clean_message = re.sub(r'\s+', ' ', clean_message)  # Clean up extra spaces
        if clean_message:
            getattr(logging, level)(clean_message)


def setup_logging(verbose: bool = False) -> None:
    """Setup logging configuration."""
    level = logging.DEBUG if verbose else logging.INFO
    
    # Create formatter
    formatter = logging.Formatter(
        '%(asctime)s [%(levelname)s] %(message)s',
        datefmt='%H:%M:%S'
    )
    
    # Setup console handler with Windows-compatible encoding
    console_handler = logging.StreamHandler()
    console_handler.setFormatter(formatter)
    
    # Setup file handler with UTF-8 encoding
    file_handler = logging.FileHandler('lidarr-cleanup.log', encoding='utf-8')
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
        help='Lidarr URL (e.g., http://localhost:8686). Can also be set via LIDARR_URL environment variable.'
    )
    
    parser.add_argument(
        '--api-key', 
        help='Lidarr API key (found in Settings > General). Can also be set via LIDARR_API_KEY environment variable.'
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
    
    # Load .env file if it exists
    load_env_file()
    
    # Get URL and API key from args or environment
    url = args.url or os.getenv('LIDARR_URL')
    api_key = args.api_key or os.getenv('LIDARR_API_KEY')
    
    # Validate required parameters
    if not url:
        safe_print("❌ Error: Lidarr URL not provided. Use --url or set LIDARR_URL environment variable.")
        return 1
    
    if not api_key:
        safe_print("❌ Error: Lidarr API key not provided. Use --api-key or set LIDARR_API_KEY environment variable.")
        return 1
    
    # Setup logging
    setup_logging(args.verbose)
    
    try:
        print("🎵 Lidarr Duplicate Artist Cleanup Script")
    except UnicodeEncodeError:
        print("Lidarr Duplicate Artist Cleanup Script")
    print("=" * 45)
    
    # Initialize API client
    api = LidarrAPI(url, api_key)
    
    # Test connection
    if not api.test_connection():
        safe_print("❌ Failed to connect to Lidarr. Check your URL and API key.")
        return 1
    
    try:
        # Get all artists
        artists = api.get_artists()
        
        if not artists:
            safe_print("ℹ️  No artists found in Lidarr.")
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
            safe_print(f"\n⚠️  WARNING: This will permanently remove duplicate artists from Lidarr!")
            print("📁 Files will NOT be deleted from disk")
            print("🚫 Artists will be added to import exclusion list")
            
            response = input("\n❓ Do you want to proceed? (yes/no): ").strip().lower()
            if response not in ('yes', 'y'):
                safe_print("❌ Operation cancelled by user.")
                return 0
        
        # Perform cleanup
        successful, failed = cleanup_duplicates(api, duplicates, dry_run=args.dry_run)
        
        # Final summary
        if args.dry_run:
            safe_print(f"\n🧪 DRY RUN COMPLETE")
            safe_print(f"💡 Run without --dry-run to actually remove {successful} duplicates")
        else:
            safe_print(f"\n🏁 CLEANUP COMPLETE")
            safe_print(f"✅ Successfully removed: {successful} artists")
            if failed > 0:
                safe_print(f"❌ Failed to remove: {failed} artists")
            safe_print(f"📋 Check lidarr-cleanup.log for details")
        
        return 0 if failed == 0 else 1
        
    except Exception as e:
        safe_log(f"❌ Script failed with error: {e}", 'error')
        if args.verbose:
            logging.exception("Full error details:")
        return 1


if __name__ == '__main__':
    sys.exit(main())