msbuild ProcessTracer.sln /p:Configuration=Debug /p:Platform="x64"
msbuild ProcessTracer.sln /p:Configuration=Debug /p:Platform="x86"

xcopy /E /I /Y ".\ProcessTracer\bin\x86\Debug\net8.0-windows\*" ".\Debug\"