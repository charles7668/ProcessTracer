#pragma once

struct HookInfo
{
	char dll_path[MAX_PATH];
	char exe_name[MAX_PATH];
	int process_tracer_pid;
	char process_tracer_pid_string_buffer[10];
	bool can_elevate = true;
};

HookInfo* GetHookInfoInstance();
