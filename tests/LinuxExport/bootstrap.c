/* Stand-in for TheChase.exe, the bootstrap Steam starts: it starts the shipping executable and waits for it. */
#include <windows.h>
#include <wchar.h>

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR command, int show)
{
    wchar_t folder[MAX_PATH], game[MAX_PATH], line[2 * MAX_PATH + 16];
    STARTUPINFOW startup = { sizeof(startup) };
    PROCESS_INFORMATION process;
    DWORD code = 3;
    GetModuleFileNameW(NULL, folder, MAX_PATH);
    *wcsrchr(folder, L'\\') = 0;
    swprintf(game, MAX_PATH, L"%ls\\TheChase\\Binaries\\Win64\\TheChase-Win64-Shipping.exe", folder);
    swprintf(line, 2 * MAX_PATH + 16, L"\"%ls\" TheChase", game);
    if (!CreateProcessW(game, line, NULL, NULL, FALSE, 0, NULL, NULL, &startup, &process)) return 2;
    WaitForSingleObject(process.hProcess, INFINITE);
    GetExitCodeProcess(process.hProcess, &code);
    return (int)code;
}
