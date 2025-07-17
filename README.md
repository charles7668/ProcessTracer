# ProcessTracer

A process monitor to trace process events using [Detours](https://github.com/microsoft/Detours)

## Table of Contents

- [ProcessTracer](#processtracer)
  - [Table of Contents](#table-of-contents)
  - [Usage](#usage)
    - [Flags](#flags)
    - [Show Help](#show-help)
    - [Using Executable File Without Arguments](#using-executable-file-without-arguments)
    - [Using Executable File With Arguments](#using-executable-file-with-arguments)
  - [Build](#build)
    - [Prerequisites](#prerequisites)
    - [Run Build Script](#run-build-script)

## Usage

### Flags

```shell
  -f, --file       Executable file to be monitored

  -a, --args       Arguments for your executable

  -o, --output     Output file for tracing results; if not set, output is shown in the console

  -e, --error      Error output file path; if not set, output is shown in the console

      --hide       Hide the console window

      --help       Display this help screen

      --version    Display version information
```

### Show Help

```shell
ProcessTracer.exe --help
```

### Using Executable File Without Arguments

```shell
ProcessTracer.exe -f <target-exe-path>
```

### Using Executable File With Arguments

> 💡 **Note:** If your arguments start with a dash (`-`), do **not** include a space after `-a`. Use `-a"your args"` instead.

```shell
ProcessTracer.exe -f <target-exe-path> -a"your args"
```

## Build

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)

### Run Build Script

Execute the build script. The output will be placed in the `bin` folder.

```shell
.\build.bat
```
