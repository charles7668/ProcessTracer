#include "pch.h"
#include "origin.h"
#include "hook_func.h"

#include <bitset>
#include <detours.h>
#include <ios>
#include <Psapi.h>

#include "constants.h"
#include "hook_info.h"
#include "logger.h"
#include "utils.h"

namespace
{
	const char* permission_request_str = "Permission Request";

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

	VOID TryLogFileName(const char* func_name, HANDLE handle)
	{
		wchar_t buffer[MAX_PATH];
		DWORD result = GetFinalPathNameByHandle(
			handle,
			buffer,
			MAX_PATH,
			FILE_NAME_NORMALIZED
		);
		if (result > 0)
		{
			std::wstring file_path(buffer, result);
			LogHookInfo(func_name, (std::string("Target:") + std::string(file_path.begin(), file_path.end())).c_str());
		}
	}

	std::wstring GetFileNameFromHandle(HANDLE hFile)
	{
		BYTE buffer[300] = {0};
		ULONG returnLength = 0;

		NTSTATUS status = NtQueryObject(
			hFile,
			(OBJECT_INFORMATION_CLASS)ObjectNameInformation,
			buffer,
			sizeof(buffer),
			&returnLength
		);

		if (status != 0)
		{
			return L"";
		}

		POBJECT_NAME_INFORMATION pNameInfo = (POBJECT_NAME_INFORMATION)buffer;
		if (pNameInfo->Name.Buffer && pNameInfo->Name.Length > 0)
		{
			return std::wstring(pNameInfo->Name.Buffer, pNameInfo->Name.Length / sizeof(WCHAR));
		}

		return L"";
	}

	bool IsSectionFileBacked(HANDLE sectionHandle)
	{
		SECTION_BASIC_INFORMATION info = {};
		NTSTATUS status = NtQuerySection(
			sectionHandle,
			SectionBasicInformation,
			&info,
			sizeof(info),
			nullptr
		);

		if (status != 0) return false;

		return (info.AllocationAttributes & SEC_FILE) != 0;
	}

	bool IsWriteAccess(ACCESS_MASK desiredAccess, ULONG createDisposition)
	{
		bool hasWriteFlags =
			(desiredAccess & GENERIC_WRITE) ||
			(desiredAccess & FILE_WRITE_DATA) ||
			(desiredAccess & FILE_APPEND_DATA) ||
			(desiredAccess & FILE_WRITE_ATTRIBUTES) ||
			(desiredAccess & DELETE);

		bool dispositionImpliesWrite =
		(createDisposition == FILE_SUPERSEDE ||
			createDisposition == FILE_CREATE ||
			createDisposition == FILE_OVERWRITE ||
			createDisposition == FILE_OVERWRITE_IF);

		return hasWriteFlags || dispositionImpliesWrite;
	}

	bool IsWritableProtection(ULONG win32Protect)
	{
		return (win32Protect == PAGE_READWRITE ||
			win32Protect == PAGE_EXECUTE_READWRITE ||
			win32Protect == PAGE_WRITECOPY ||
			win32Protect == PAGE_EXECUTE_WRITECOPY);
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
	auto msg = "[ApplicationName] " + ConvertWStringToString(lpApplicationName) + ", [CommandLine] " +
		ConvertWStringToString(lpCommandLine);
	LogHookInfo(hook_func_name.c_str(), msg.c_str());
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
		// if 740, it means the process requires elevation
		if (GetLastError() == 740)
		{
			LogInfo(permission_request_str);
		}
		else
		{
			LogHookError(hook_func_name.c_str(),
			             ("RealCreateProcessInternalW failed with " + std::to_string(GetLastError())).c_str());
		}
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
	std::string payload = std::to_string(hook_info->process_tracer_pid) + " " +
		(hook_info->can_elevate ? "0" : "1");
	if (!DetourCopyPayloadToProcess(lpProcessInformation->hProcess, GUID_PIPE_HANDLE,
	                                payload.c_str(),
	                                payload.length()))
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

HANDLE WINAPI HookCreateFileMappingW(HANDLE hFile, LPSECURITY_ATTRIBUTES lpFileMappingAttributes, DWORD flProtect,
                                     DWORD dwMaximumSizeHigh, DWORD dwMaximumSizeLow, LPCWSTR lpName)
{
	LogHookInfo("CreateFileMappingW", "called");
	const auto file_name = GetFileNameFromHandle(hFile);
	LogHookInfo("CreateFileMappingW", std::string(file_name.begin(), file_name.end()).c_str());

	return RealCreateFileMappingW(
		hFile,
		lpFileMappingAttributes,
		flProtect,
		dwMaximumSizeHigh,
		dwMaximumSizeLow,
		lpName
	);
}


NTSTATUS NTAPI HookZwWriteFile(
	HANDLE FileHandle,
	HANDLE Event,
	PIO_APC_ROUTINE ApcRoutine,
	PVOID ApcContext,
	PIO_STATUS_BLOCK IoStatusBlock,
	PVOID Buffer,
	ULONG Length,
	PLARGE_INTEGER ByteOffset,
	PULONG Key)
{
	LogHookInfo("ZwWriteFile", "called");

	return ZwWriteFile(
		FileHandle,
		Event,
		ApcRoutine,
		ApcContext,
		IoStatusBlock,
		Buffer,
		Length,
		ByteOffset,
		Key
	);
}

NTSTATUS NTAPI HookNtWriteFile(HANDLE FileHandle,
                               HANDLE Event,
                               PIO_APC_ROUTINE ApcRoutine,
                               PVOID ApcContext,
                               PIO_STATUS_BLOCK IoStatusBlock,
                               PVOID Buffer,
                               ULONG Length,
                               PLARGE_INTEGER ByteOffset,
                               PULONG Key)
{
	const auto status = NtWriteFile(
		FileHandle,
		Event,
		ApcRoutine,
		ApcContext,
		IoStatusBlock,
		Buffer,
		Length,
		ByteOffset,
		Key
	);
	const auto name = ConvertWStringToString(GetFileNameFromHandle(FileHandle).c_str());
	LogHookInfo("NtWriteFile", name.c_str());
	return status;
}

NTSTATUS NTAPI HookNtCreateSection(PHANDLE SectionHandle, ACCESS_MASK DesiredAccess,
                                   POBJECT_ATTRIBUTES ObjectAttributes, PLARGE_INTEGER MaximumSize,
                                   ULONG SectionPageProtection,
                                   ULONG AllocationAttributes, HANDLE FileHandle)
{
	// LogHookInfo("NtCreateSection", "called");
	return NtCreateSection(
		SectionHandle,
		DesiredAccess,
		ObjectAttributes,
		MaximumSize,
		SectionPageProtection,
		AllocationAttributes,
		FileHandle
	);
}

NTSTATUS NTAPI HookZwCreateSection(
	PHANDLE SectionHandle,
	ACCESS_MASK DesiredAccess,
	POBJECT_ATTRIBUTES ObjectAttributes,
	PLARGE_INTEGER MaximumSize,
	ULONG SectionPageProtection,
	ULONG AllocationAttributes,
	HANDLE FileHandle
)
{
	// LogHookInfo("ZwCreateSection", "called");
	return ZwCreateSection(
		SectionHandle,
		DesiredAccess,
		ObjectAttributes,
		MaximumSize,
		SectionPageProtection,
		AllocationAttributes,
		FileHandle
	);
}

NTSTATUS NTAPI HookNtCreateSectionEx(
	_Out_ PHANDLE SectionHandle,
	_In_ ACCESS_MASK DesiredAccess,
	_In_opt_ POBJECT_ATTRIBUTES ObjectAttributes,
	_In_opt_ PLARGE_INTEGER MaximumSize,
	_In_ ULONG SectionPageProtection,
	_In_ ULONG AllocationAttributes,
	_In_opt_ HANDLE FileHandle,
	_Inout_updates_opt_(ExtendedParameterCount) PMEM_EXTENDED_PARAMETER ExtendedParameters,
	_In_ ULONG ExtendedParameterCount
)
{
	// LogHookInfo("NtCreateSectionEx", "called");
	return NtCreateSectionEx(
		SectionHandle,
		DesiredAccess,
		ObjectAttributes,
		MaximumSize,
		SectionPageProtection,
		AllocationAttributes,
		FileHandle,
		ExtendedParameters,
		ExtendedParameterCount
	);
}


NTSTATUS __stdcall HookNtCreateFile(PHANDLE FileHandle, ACCESS_MASK DesiredAccess, POBJECT_ATTRIBUTES ObjectAttributes,
                                    PIO_STATUS_BLOCK IoStatusBlock, PLARGE_INTEGER AllocationSize, ULONG FileAttributes,
                                    ULONG ShareAccess,
                                    ULONG CreateDisposition, ULONG CreateOptions, PVOID EaBuffer, ULONG EaLength)
{
	auto status = NtCreateFile(
		FileHandle,
		DesiredAccess,
		ObjectAttributes,
		IoStatusBlock,
		AllocationSize,
		FileAttributes,
		ShareAccess,
		CreateDisposition,
		CreateOptions,
		EaBuffer,
		EaLength
	);
	auto hook_info = GetHookInfoInstance();
	if (*FileHandle && ObjectAttributes->ObjectName->Length > 0 &&
		!EndsWith(ConvertWStringToString(ObjectAttributes->ObjectName->Buffer),
		          "ProcessTracerPipe:" + std::string(hook_info->process_tracer_pid_string_buffer)))
	{
		auto file_name = ConvertWStringToString(ObjectAttributes->ObjectName->Buffer);
		std::bitset<32> binary(DesiredAccess);
		auto msg = "[DesiredAccess] " + binary.to_string() + ", [FileName] " + file_name;
		LogHookNtCreateProcessInfo("NtCreateFile", msg.c_str());
	}
	return status;
}

NTSTATUS NTAPI HookNtMapViewOfSection(HANDLE SectionHandle, HANDLE ProcessHandle, PVOID* BaseAddress,
                                      ULONG_PTR ZeroBits, SIZE_T CommitSize, PLARGE_INTEGER SectionOffset,
                                      PSIZE_T ViewSize,
                                      SECTION_INHERIT InheritDisposition, ULONG AllocationType, ULONG PageProtection)
{
	auto status = NtMapViewOfSection(
		SectionHandle,
		ProcessHandle,
		BaseAddress,
		ZeroBits,
		CommitSize,
		SectionOffset,
		ViewSize,
		InheritDisposition,
		AllocationType,
		PageProtection
	);
	// if (IsSectionFileBacked(SectionHandle) && IsWritableProtection(PageProtection))
	// {
	// 	TCHAR path[MAX_PATH];
	// 	if (GetMappedFileNameW(GetCurrentProcess(), *BaseAddress, path, MAX_PATH))
	// 	{
	// 		auto file_name = ConvertWStringToString(path);
	// 		LogHookInfo("NtMapViewOfSection", file_name.c_str());
	// 	}
	// }
	return status;
}

NTSTATUS NTAPI HookNtCreateUserProcess(PHANDLE ProcessHandle, PHANDLE ThreadHandle,
                                       ACCESS_MASK ProcessDesiredAccess, ACCESS_MASK ThreadDesiredAccess,
                                       PCOBJECT_ATTRIBUTES ProcessObjectAttributes,
                                       PCOBJECT_ATTRIBUTES ThreadObjectAttributes, ULONG ProcessFlags,
                                       ULONG ThreadFlags,
                                       PRTL_USER_PROCESS_PARAMETERS ProcessParameters, PPS_CREATE_INFO CreateInfo,
                                       PPS_ATTRIBUTE_LIST AttributeList)
{
	LogHookInfo("NtCreateUserProcess", "called");
	return NtCreateUserProcess(
		ProcessHandle,
		ThreadHandle,
		ProcessDesiredAccess,
		ThreadDesiredAccess,
		ProcessObjectAttributes,
		ThreadObjectAttributes,
		ProcessFlags,
		ThreadFlags,
		ProcessParameters,
		CreateInfo,
		AttributeList
	);
}

BOOL WINAPI HookShellExecuteExW(SHELLEXECUTEINFOW* pExecInfo)
{
	auto verb = ConvertWStringToString(pExecInfo->lpVerb);
	std::string msg = "verb:" + verb;
	LogHookInfo("ShellExecuteExW", msg.c_str());
	auto hook_info = GetHookInfoInstance();
	if (hook_info->can_elevate && StartsWith(verb, "runas"))
	{
		LogHookError("ShellExecuteExW",
			"ProcessTracerCore can elevate, but ShellExecuteExW called with runas verb.");
		LogInfo(permission_request_str);
		TerminateProcess(GetCurrentProcess(), 0);
		return FALSE;
	}
	return RealShellExecuteExW(pExecInfo);
}

NTSTATUS __stdcall HookNtSetInformationFile(HANDLE FileHandle, PIO_STATUS_BLOCK IoStatusBlock, PVOID FileInformation,
                                            ULONG Length, FILE_INFORMATION_CLASS FileInformationClass)
{
	auto file_name = ConvertWStringToString(GetFileNameFromHandle(FileHandle).c_str());
	LogHookInfo("NtSetInformationFile", file_name.c_str());
	return NtSetInformationFile(
		FileHandle,
		IoStatusBlock,
		FileInformation,
		Length,
		FileInformationClass
	);
}
