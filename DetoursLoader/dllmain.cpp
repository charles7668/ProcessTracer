#include "pch.h"

#include <string>

#include "DetoursLoader.h"
#include "constants.h"

namespace
{
	DWORD create_error;
}

DWORD EXPORT WINAPI GetDetourCreateProcessError()
{
	return create_error;
}

BOOL WINAPI DetourCreateProcessWithDllAWrap(_In_opt_ LPCSTR lpApplicationName,
                                            _Inout_opt_ LPSTR lpCommandLine,
                                            _In_opt_ LPSECURITY_ATTRIBUTES lpProcessAttributes,
                                            _In_opt_ LPSECURITY_ATTRIBUTES lpThreadAttributes,
                                            _In_ BOOL bInheritHandles,
                                            _In_ DWORD dwCreationFlags,
                                            _In_opt_ LPVOID lpEnvironment,
                                            _In_opt_ LPCSTR lpCurrentDirectory,
                                            _In_ LPSTARTUPINFOA lpStartupInfo,
                                            _Out_ LPPROCESS_INFORMATION lpProcessInformation,
                                            _In_ DWORD nDlls,
                                            _In_ LPCSTR* lpDllName,
                                            _In_ LPCSTR pipeHandle)
{
	DWORD dwNewCreationFlag = dwCreationFlags | CREATE_SUSPENDED;
	create_error = 0;
	if (!DetourCreateProcessWithDllsA(
		lpApplicationName,
		lpCommandLine,
		lpProcessAttributes,
		lpThreadAttributes,
		bInheritHandles,
		dwNewCreationFlag,
		lpEnvironment,
		lpCurrentDirectory,
		lpStartupInfo,
		lpProcessInformation,
		nDlls,
		lpDllName,
		nullptr))
	{
		auto err = GetLastError();
		fprintf(stderr, "DetourCreateProcessWithDllsA failed with error code: %lu\n", err);
		create_error = err;
		return FALSE;
	}
	if (DetourCopyPayloadToProcess(lpProcessInformation->hProcess, GUID_PIPE_HANDLE, pipeHandle, strlen(pipeHandle) + 1)
		== FALSE)
	{
		auto err = GetLastError();
		fprintf(stderr, "DetourCopyPayloadToProcess failed with error code: %lu\n", err);
		create_error = err;
		TerminateProcess(lpProcessInformation->hProcess, 0);
		CloseHandle(lpProcessInformation->hProcess);
		CloseHandle(lpProcessInformation->hThread);
		return FALSE;
	}
	if (!(dwCreationFlags & CREATE_SUSPENDED))
	{
		ResumeThread(lpProcessInformation->hThread);
	}
	return TRUE;
}

// NOLINTFIXLINE
BOOL APIENTRY DllMain(HMODULE hModule,
                      DWORD ul_reason_for_call,
                      LPVOID lpReserved
)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	default: ;
	}
	return TRUE;
}
