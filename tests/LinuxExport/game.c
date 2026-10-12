/* Stand-in for TheChase-Win64-Shipping.exe: a window, a character marker in memory and a launch report. */
#include <windows.h>
#include <stdio.h>

volatile char marker[64] = "KPC-CHARACTER:Aloisa Test";

static LRESULT CALLBACK window_proc(HWND window, UINT message, WPARAM w, LPARAM l)
{
    if (message == WM_CLOSE) { DestroyWindow(window); return 0; }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, w, l);
}

static void report(void)
{
    static const wchar_t *names[] = { L"SteamAppId", L"SteamGameId", L"SteamOverlayGameId", L"KPC_WORKER_TOKEN" };
    wchar_t path[MAX_PATH], value[512];
    HKEY key; DWORD pid = 0, size = sizeof(pid);
    if (!GetEnvironmentVariableW(L"KPC_EXPORT_CHECK_REPORT", path, MAX_PATH)) return;
    FILE *file = _wfopen(path, L"w");
    if (!file) return;
    for (int i = 0; i < 4; i++)
    {
        value[0] = 0; GetEnvironmentVariableW(names[i], value, 512);
        fwprintf(file, L"%ls=%ls\n", names[i], value);
    }
    GetCurrentDirectoryW(512, value);
    fwprintf(file, L"cwd=%ls\n", value);
    if (!RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Valve\\Steam\\ActiveProcess", 0, KEY_READ, &key))
    {
        RegQueryValueExW(key, L"pid", NULL, NULL, (BYTE *)&pid, &size);
        RegCloseKey(key);
    }
    /* Proton's Steam helper creates these for a game; the Steam API looks for them. */
    HANDLE ipc = OpenEventW(SYNCHRONIZE, FALSE, L"Global\\Valve_SteamIPC_Class");
    fwprintf(file, L"steamPid=%lu\nsteamIpc=%d\n", pid, ipc != NULL);
    fclose(file);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR command, int show)
{
    WNDCLASSW type = { 0 };
    MSG message;
    report();
    type.lpfnWndProc = window_proc; type.hInstance = instance; type.lpszClassName = L"KpcExportCheckGame";
    RegisterClassW(&type);
    ShowWindow(CreateWindowW(type.lpszClassName, L"KurtzPel stand-in", WS_OVERLAPPEDWINDOW, 0, 0, 320, 200, NULL, NULL, instance, NULL), SW_SHOW);
    while (GetMessageW(&message, NULL, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    return marker[0] == 'K' ? 0 : 1;
}
