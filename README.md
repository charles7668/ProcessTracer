# ProcessTracer

A process monitor to trace process events.

- [ProcessTracer](#processtracer)
  - [Usage](#usage)
    - [Flags](#flags)
    - [Show Help](#show-help)
    - [Using PID](#using-pid)
    - [Using Exe File Path](#using-exe-file-path)
  - [Build](#build)
    - [Prerequisites](#prerequisites)
    - [Run Build Script](#run-build-script)

## Usage

Program needs admin privileges

### Flags

```shell
  -p, --pid             Your process id for tracing

  -f, --file            Your module file for tracing , if set pid then this setting will be ignored

  -w, --wait            Waiting time for attach process when using --file option; if set to 0, the time is infinite

  --hide                Hide console window

  --disable-registry    Disable registry event

  --disable-fileio      Disable file io event

  --help                Display this help screen.

  --version             Display version information.
```

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
