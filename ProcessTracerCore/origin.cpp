#include "pch.h"
#include "origin.h"

#include "hook_func.h"
#include "_win32.h"

DWORD (WINAPI*RealGetModuleFileNameW)(HMODULE module_handle,
                                      LPWSTR filename,
                                      DWORD size)
	= GetModuleFileNameW;

DWORD (WINAPI*RealGetModuleFileNameA)(HMODULE module_handle,
                                      LPSTR filename,
                                      DWORD size)
	= GetModuleFileNameA;

VOID (WINAPI*RealExitProcess)(UINT exit_code) = ExitProcess;

decltype(&CreateFileMappingW) RealCreateFileMappingW = nullptr;

CreateProcessInternalWFn RealCreateProcessInternalW = nullptr;

BOOL (WINAPI*RealShellExecuteExW)(SHELLEXECUTEINFOW* pExecInfo) = ShellExecuteExW;
