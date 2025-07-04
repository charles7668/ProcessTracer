#include "pch.h"
#include "origin.h"
#include "hook_func.h"
#include <detours.h>
#include "constants.h"
#include "hook_info.h"
#include "logger.h"

namespace
{
	BOOL WINAPI MineCreateProcessInternalW(
		LPCWSTR lpApplicationName,
		LPWSTR lpCommandLine,
		LPSECURITY_ATTRIBUTES lpProcessAttributes,
		LPSECURITY_ATTRIBUTES lpThreadAttributes,
		BOOL bInheritHandles,
		DWORD dwCreationFlags,
		LPVOID lpEnvironment,
		LPCWSTR lpCurrentDirectory,
		LPSTARTUPINFOW lpStartupInfo,
		LPPROCESS_INFORMATION lpProcessInformation
	)
	{
		return RealCreateProcessInternalW(nullptr, lpApplicationName, lpCommandLine,
		                                  lpProcessAttributes, lpThreadAttributes, bInheritHandles,
		                                  dwCreationFlags | CREATE_SUSPENDED, lpEnvironment, lpCurrentDirectory,
		                                  lpStartupInfo, lpProcessInformation, nullptr);
	}
}


BOOL WINAPI HookCreateProcessInternalW(
	HANDLE hUserToken,
	LPCWSTR lpApplicationName,
	LPWSTR lpCommandLine,
	LPSECURITY_ATTRIBUTES lpProcessAttributes,
	LPSECURITY_ATTRIBUTES lpThreadAttributes,
	BOOL bInheritHandles,
	DWORD dwCreationFlags,
	LPVOID lpEnvironment,
	LPCWSTR lpCurrentDirectory,
	LPSTARTUPINFOW lpStartupInfo,
	LPPROCESS_INFORMATION lpProcessInformation,
	OPTIONAL PHANDLE hRestrictedUserToken
)
{
	const std::string hook_func_name = "CreateProcessInternalW";
	LogHookInfo(hook_func_name.c_str(), "called");

	std::wstring temp = (L"Application Name: " + std::wstring(lpApplicationName ? lpApplicationName : L""));
	LogHookInfo(hook_func_name.c_str(), std::string(temp.begin(), temp.end()).c_str());
	temp = (L"Command Line: " + std::wstring(lpCommandLine ? lpCommandLine : L""));
	LogHookInfo(hook_func_name.c_str(), std::string(temp.begin(), temp.end()).c_str());
	if (!RealCreateProcessInternalW(
		hUserToken,
		lpApplicationName,
		lpCommandLine,
		lpProcessAttributes,
		lpThreadAttributes,
		bInheritHandles,
		dwCreationFlags | CREATE_SUSPENDED, // Ensure the process is created suspended
		lpEnvironment,
		lpCurrentDirectory,
		lpStartupInfo,
		lpProcessInformation,
		hRestrictedUserToken
	))
	{
		LogHookError(hook_func_name.c_str(), ("RealCreateProcessInternalW failed with " + std::to_string(GetLastError())).c_str());
		return FALSE;
	}
	const auto hook_info = GetHookInfoInstance();
	LPCSTR sz = hook_info->dll_path;

	if (!DetourUpdateProcessWithDll(lpProcessInformation->hProcess, &sz, 1) &&
		!DetourProcessViaHelperW(lpProcessInformation->dwProcessId,
		                         sz,
		                         MineCreateProcessInternalW))
	{
		LogHookError(hook_func_name.c_str(), "DetourUpdateProcessWithDll or DetourProcessViaHelperW failed");
		TerminateProcess(lpProcessInformation->hProcess, ~0u);
		CloseHandle(lpProcessInformation->hProcess);
		CloseHandle(lpProcessInformation->hThread);
		return FALSE;
	}
	if (!DetourCopyPayloadToProcess(lpProcessInformation->hProcess, GUID_PIPE_HANDLE,
	                                hook_info->process_tracer_pid_string_buffer,
	                                strlen(hook_info->process_tracer_pid_string_buffer) + 1))
	{
		LogHookError(hook_func_name.c_str(),
		             "DetourCopyPayloadToProcess failed to copy ProcessTracer pid payload to process");
	}
	LogHookInfo(hook_func_name.c_str(), ("Process created successfully with PID: " +
		            std::to_string(lpProcessInformation->dwProcessId)).c_str());
	if (!(dwCreationFlags & CREATE_SUSPENDED))
	{
		ResumeThread(lpProcessInformation->hThread);
	}

	return TRUE;
}

VOID WINAPI HookExitProcess(UINT exit_code)
{
	DWORD current_pid = GetCurrentProcessId();
	LogHookInfo("ExitProcess", (std::to_string(current_pid) + " Exited").c_str());
	RealExitProcess(exit_code);
}
