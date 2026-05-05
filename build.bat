@echo off
pyinstaller --onedir --windowed --name autosaver --icon assets/icon.ico src/main.py
echo Build complete. Output in dist\autosaver\
pause
