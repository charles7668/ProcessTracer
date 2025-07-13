#pragma once
#include "pch.h"
#include "_win32.h"
#include <shellapi.h>

extern DWORD (WINAPI*RealGetModuleFileNameW)(HMODULE module_handle,
                                             LPWSTR filename,
                                             DWORD size);

extern DWORD (WINAPI*RealGetModuleFileNameA)(HMODULE module_handle,
                                             LPSTR filename,
                                             DWORD size);

extern VOID (WINAPI *RealExitProcess)(UINT exit_code);

extern decltype(&CreateFileMappingW) RealCreateFileMappingW;

extern CreateProcessInternalWFn RealCreateProcessInternalW;

extern BOOL (WINAPI *RealShellExecuteExW)(_Inout_ SHELLEXECUTEINFOW* pExecInfo);
