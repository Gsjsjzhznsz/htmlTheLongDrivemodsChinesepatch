# htmlTheLongDrivemodsChinesepatch
# The Long Drive Mod Installer

A comprehensive mod management tool for The Long Drive game, featuring a user-friendly graphical interface for browsing, installing, updating, and uninstalling mods.

## Features

### 🔍 Mod Management
- **Multi-source Support**: Automatically fetches mod lists from multiple sources with fallback support
- **Search & Filter**: Quickly find mods by name or author, filter by category
- **Dual View Mode**: Switch between grid view and list view for better browsing experience
- **Mod Details**: View complete mod information including description, dependencies, and images

### 📦 Installation System
- **One-Click Install**: Install mods with a single click
- **Dependency Handling**: Automatically installs required dependencies
- **Progress Tracking**: Real-time download progress with speed and ETA display
- **ZIP/DLL Support**: Handles both compressed archives and direct DLL files
- **Batch Installation**: Install entire modpacks with pre-configured mod collections

### 🗑️ Mod Management
- **Precise Uninstallation**: Only removes the mod's DLL files, preserving configuration files
- **Update Detection**: Automatically detects when newer versions are available
- **Version Tracking**: Keeps track of installed mod versions

### 🎨 User Interface
- **Modern Design**: Clean, dark-themed interface with smooth animations
- **Responsive Layout**: Works on both desktop and mobile devices
- **Toast Notifications**: Non-intrusive feedback for all operations
- **Download Manager**: Floating panel showing all active downloads

## Technical Details

### Architecture
- **Backend**: Python Flask with threading support
- **Frontend**: Pure HTML/CSS/JavaScript, no external dependencies
- **Data Storage**: JSON-based manifest files for precise file tracking
- **Network**: Retry mechanism with 5 attempts and 10-second timeout

### Supported Mod Formats
- Direct DLL files
- ZIP archives (extracted to Mods folder)
- Modpacks (TXT files containing list of mod filenames)

### File Structure
