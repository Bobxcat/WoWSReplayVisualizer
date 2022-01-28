# WoWSReplayVisualizer

## How to compile:
1. Download the source code
2. Open a terminal in ```WoWSReplayVisualizer/ReplayVisualizer``` and run the command ```dotnet build```
3. From there, navigate to ```WoWSReplayVisualizer/ReplayVisualizer/bin/Debug/``` -- this is where the compiled ```ReplayVisualizer.exe``` will be

## How to run:
1. In the same directory as ReplayVisualizer.exe, open a terminal window
2. Run the command ```ReplayVisualizer [filename]``` to render a video from the .jl file specified by the filename. Giving no filename will make the program read from ```in.jl```
3. The program will output a video of the game to ```out.mkv```
