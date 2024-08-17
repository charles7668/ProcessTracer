dotnet publish ProcessTracer -f net8.0-windows -o "./bin/ProcessTracer x86" -c Release -a x86
dotnet publish ProcessTracer -f net8.0-windows -o "./bin/ProcessTracer" -c Release -a x64
del ".\bin\ProcessTracer\*.pdb"
del ".\bin\ProcessTracer x86\*.pdb"
