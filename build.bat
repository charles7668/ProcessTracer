msbuild ProcessTracer.sln /p:Configuration=Release /p:Platform="x64"
msbuild ProcessTracer.sln /p:Configuration=Release /p:Platform="x86"

xcopy /E /I /Y ".\ProcessTracer\bin\x86\Release\net8.0-windows\*" ".\Release\"