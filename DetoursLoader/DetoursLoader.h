#pragma once
#include "pch.h"
#include "detours.h"

#define EXPORT __declspec(dllexport)

extern "C" {
BOOL EXPORT WINAPI DetourCreateProcessWithDllWWrap(_In_opt_ LPCWSTR lpApplicationName,
                                                   _Inout_opt_ LPWSTR lpCommandLine,
                                                   _In_opt_ LPSECURITY_ATTRIBUTES lpProcessAttributes,
                                                   _In_opt_ LPSECURITY_ATTRIBUTES lpThreadAttributes,
                                                   _In_ BOOL bInheritHandles,
                                                   _In_ DWORD dwCreationFlags,
                                                   _In_opt_ LPVOID lpEnvironment,
                                                   _In_opt_ LPCWSTR lpCurrentDirectory,
                                                   _In_ LPSTARTUPINFOW lpStartupInfo,
                                                   _Out_ LPPROCESS_INFORMATION lpProcessInformation,
                                                   _In_ DWORD nDlls,
                                                   _In_ LPCSTR* lpDllName,
                                                   _In_ LPCSTR pipeHandle);
DWORD EXPORT WINAPI GetDetourCreateProcessError();
}
