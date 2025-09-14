# Lidarr Duplicate Artist Cleanup Scripts

🎵 **Automatically detect and remove duplicate artists from Lidarr**

These scripts solve the problem of duplicate artists showing up with suffixes like "Artist (2)", "Artist (3)", etc., which can happen when import list plugins create duplicates.

## 🚀 Quick Start

### Windows Users

1. **Download the scripts** to a folder on your computer
2. **Copy `.env.example` to `.env`** and edit it:
```text
   LIDARR_URL=http://localhost:8686
   LIDARR_API_KEY=your_actual_api_key_here
   ```
3. **Double-click `cleanup-duplicates.bat`** to run

### Linux/macOS Users

1. **Download the scripts** to a folder
2. **Copy `.env.example` to `.env`** and edit it:
```text
   LIDARR_URL=http://localhost:8686
   LIDARR_API_KEY=your_actual_api_key_here
   ```
3. **Run the script**:
   ```bash
   ./cleanup-duplicates.sh
   ```

### Getting Your API Key

1. Open Lidarr web interface
2. Go to **Settings** > **General**
3. Copy the **API Key** value
4. Paste it into your script configuration

## 📋 What It Does

### Detects These Duplicate Patterns:
- `Tonic` vs `Tonic (2)` → Removes `Tonic (2)`
- `Travis Scott` vs `Travis Scott (2)` → Removes `Travis Scott (2)`
- `Walk The Moon` vs `Walk The Moon (2)` → Removes `Walk The Moon (2)`

### Smart Matching:
- **Case insensitive**: `artist` matches `Artist`
- **"The" prefix handling**: `The Beatles` matches `Beatles`
- **Punctuation normalization**: Handles quotes and special characters
- **Space normalization**: Multiple spaces treated as single

### Safety Features:
- **Dry-run by default**: Shows what would be removed without actually doing it
- **Preview mode**: See exactly what will be deleted before confirming
- **Files preserved**: Only removes from Lidarr, doesn't delete music files
- **Import exclusion**: Adds removed artists to exclusion list to prevent re-adding

## 📖 Usage Examples

### Basic Usage (Safe Preview)
```bash
# Windows
cleanup-duplicates.bat

# Linux/macOS
./cleanup-duplicates.sh
```

### Actually Remove Duplicates
```bash
# Windows
cleanup-duplicates.bat --no-dry-run

# Linux/macOS
./cleanup-duplicates.sh --no-dry-run
```

### Advanced Options
```bash
# Verbose output with detailed logging
./cleanup-duplicates.sh --verbose

# Skip confirmation prompt (dangerous!)
./cleanup-duplicates.sh --no-dry-run --auto-confirm

# Direct Python usage with custom settings
python3 cleanup-duplicates.py --url http://192.168.1.100:8686 --api-key abc123 --dry-run
```

## 📊 Example Output

```text
🎵 Lidarr Duplicate Artist Cleanup
=================================
✅ Connected to Lidarr 2.13.3.4698 - Lidarr
📡 Fetching all artists from Lidarr...
✅ Retrieved 1,247 artists
🔍 Analyzing artists for duplicates...

📊 DUPLICATE ANALYSIS RESULTS
==================================================
🎯 Found 8 base artists with duplicates
🗑️  Total duplicates to remove: 12
💾 Artists that will be kept: 8

🎵 Tonic
   ✅ Will keep original
   🗑️  Will remove 1 duplicate(s):
      1. Tonic (2) (0 albums, unmonitored)

🎵 Travis Scott
   ✅ Will keep original
   🗑️  Will remove 1 duplicate(s):
      1. Travis Scott (2) (72 albums, monitored)

🧪 DRY RUN COMPLETE
💡 Run without --dry-run to actually remove 12 duplicates
```

## ⚙️ Configuration Options

### Recommended: .env File Configuration
Copy `.env.example` to `.env` and edit:
```text
LIDARR_URL=http://localhost:8686
LIDARR_API_KEY=your_api_key_here
```

### Alternative: Command Line Arguments
```bash
python cleanup-duplicates.py --url http://localhost:8686 --api-key your_key --dry-run
```

### Alternative: Environment Variables
```bash
export LIDARR_URL="http://localhost:8686"
export LIDARR_API_KEY="your_api_key" # pragma: allowlist secret
```

## 📁 Files Created

- `lidarr-cleanup.log` - Detailed log of all operations
- Creates backup information in log before deletion

## 🛡️ Safety Features

### What's Protected:
- ✅ **Music files stay safe** - Never deletes actual music files
- ✅ **Original artists preserved** - Only removes numbered duplicates
- ✅ **Dry-run default** - Must explicitly choose to remove duplicates
- ✅ **Detailed preview** - See exactly what will happen before it happens
- ✅ **Comprehensive logging** - Full record of all actions

### What's Removed:
- ❌ **Duplicate entries only** - Artists with `(2)`, `(3)` suffixes
- ❌ **From Lidarr database** - Removes the duplicate artist entry
- ❌ **Adds to exclusion** - Prevents re-importing the same duplicate

## 🔧 Troubleshooting

### "Python not found"
- **Windows**: Install Python from https://python.org, check "Add to PATH"
- **Linux**: `sudo apt install python3 python3-pip`
- **macOS**: `brew install python3`

### "requests library not found"
```bash
pip3 install requests
```

### "Failed to connect to Lidarr"
- Check your URL is correct (include http://)
- Verify API key is correct
- Ensure Lidarr is running and accessible

### "Permission denied"
```bash
chmod +x cleanup-duplicates.sh
```

### Script hangs or errors
- Check `lidarr-cleanup.log` for detailed error information
- Try running with `--verbose` for more output
- Verify Lidarr is not currently performing other operations

## 🚨 Important Notes

### Before Running:
1. **Backup your Lidarr database** (optional but recommended)
2. **Test with dry-run first** to see what would be removed
3. **Check the preview carefully** before confirming removal

### After Running:
1. **Refresh Lidarr** - Force a refresh to update the UI
2. **Check your library** - Verify expected artists are present
3. **Import list exclusions** - Duplicates are added to prevent re-import

## 🆘 Recovery

If something goes wrong:
1. **Check the log file** `lidarr-cleanup.log` for details
2. **Re-add artists manually** if needed through Lidarr UI
3. **Import exclusions** can be removed from Lidarr settings if needed

## 📞 Support

If you encounter issues:
1. Check the log file for error details
2. Try running with `--verbose` for more information
3. Ensure your Lidarr instance is accessible and up-to-date
4. Create an issue in the Brainarr plugin repository

---

**Created by the Brainarr Plugin Team** - Saving you time, one duplicate at a time! 🎵✨
