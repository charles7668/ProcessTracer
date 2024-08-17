# ProcessTracer

A process monitor to trace process events.

- [ProcessTracer](#processtracer)
  - [Usage](#usage)
    - [Show Help](#show-help)
    - [Using PID](#using-pid)
    - [Using Exe File Path](#using-exe-file-path)
  - [Build](#build)
    - [Prerequisites](#prerequisites)
    - [Run Build Script](#run-build-script)

## Usage

### Show Help

```shell
ProcessTracer.exe -h
```

### Using PID

```shell
ProcessTracer.exe -p 1234
```

### Using Exe File Path

```shell
ProcessTracer.exe -f <target-exe-path>
```

## Build

### Prerequisites

- [.NET 8](https://dotnet.microsoft.com/en-us/download)

### Run Build Script

Execute the build script. The output will be placed in the `bin` folder.

```shell
.\build.bat
```
