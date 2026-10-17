/*
 * Stand-in for the private listener started as node.exe -e <loader> with the in-memory payload on stdin.
 * It attaches to the game by the process ID it is given, reads the character marker from the game's
 * memory and writes the export, so the test proves that the listener can reach the game's process.
 */
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

static const wchar_t *json_string(const wchar_t *text, wchar_t *out, size_t size)
{
    size_t length = 0;
    if (*text++ != L'"') return NULL;
    while (*text && *text != L'"')
    {
        wchar_t c = *text++;
        if (c == L'\\')
        {
            switch (c = *text++)
            {
            case L'n': c = L'\n'; break;
            case L'r': c = L'\r'; break;
            case L't': c = L'\t'; break;
            case L'u': { wchar_t hex[5] = { 0 }; wcsncpy(hex, text, 4); c = (wchar_t)wcstoul(hex, NULL, 16); text += 4; break; }
            }
        }
        if (length + 1 < size) out[length++] = c;
    }
    out[length] = 0;
    return *text == L'"' ? text + 1 : NULL;
}

static int capture(DWORD pid, char *name, size_t size)
{
    static char page[1 << 16];
    MEMORY_BASIC_INFORMATION region;
    unsigned char *address = NULL;
    HANDLE game = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, FALSE, pid);
    if (!game) return 0;
    while (VirtualQueryEx(game, address, &region, sizeof(region)) == sizeof(region))
    {
        SIZE_T read = 0;
        if (region.State == MEM_COMMIT && region.RegionSize <= sizeof(page) &&
            (region.Protect & (PAGE_READWRITE | PAGE_READONLY | PAGE_WRITECOPY)) &&
            ReadProcessMemory(game, region.BaseAddress, page, region.RegionSize, &read))
            for (SIZE_T i = 0; i + 14 < read; i++)
                if (!memcmp(page + i, "KPC-CHARACTER:", 14)) { strncpy(name, page + i + 14, size - 1); CloseHandle(game); return 1; }
        address = (unsigned char *)region.BaseAddress + region.RegionSize;
    }
    CloseHandle(game);
    return 0;
}

int wmain(int argc, wchar_t **argv)
{
    static char utf8[1 << 20];
    static wchar_t payload[1 << 20], runner[MAX_PATH], pid[32], output[MAX_PATH], delay[16], loader[MAX_PATH];
    DWORD length = 0, read, total = 0, written;
    char name[64] = "", content[256];
    const wchar_t *cursor;
    HANDLE input = GetStdHandle(STD_INPUT_HANDLE), file;
    if (argc != 3 || wcscmp(argv[1], L"-e") || !GetEnvironmentVariableW(L"NODE_PATH", loader, MAX_PATH))
    { puts("KPC_ERROR The stand-in listener was not started like node.exe."); return 2; }
    if (!ReadFile(input, &length, 4, &read, NULL) || read != 4 || length >= sizeof(utf8)) return 3;
    while (total < length && ReadFile(input, utf8 + total, length - total, &read, NULL) && read) total += read;
    MultiByteToWideChar(CP_UTF8, 0, utf8, (int)total, payload, (int)(sizeof(payload) / sizeof(*payload)) - 1);
    if (!(cursor = wcsstr(payload, L"\"args\":[")) || !(cursor = json_string(cursor + 8, runner, MAX_PATH)) ||
        !(cursor = json_string(cursor + 1, pid, 32)) || !json_string(cursor + 1, output, MAX_PATH))
    { puts("KPC_ERROR The listener payload is invalid."); return 4; }
    printf("KPC_STATUS Listening to the game\n"); fflush(stdout);
    if (GetEnvironmentVariableW(L"KPC_EXPORT_CHECK_FAIL", delay, 16))
    {
        /* What Node prints for an uncaught error in the listener: the failing source line, then the message. */
        fputs("Z:\\export\\tools\\character-export\\run.js:12\n        throw new Error('private listener source');\n"
              "        ^\n\nError: stand-in attach failure\n    at attach (Z:\\export\\tools\\character-export\\run.js:12:15)\n", stderr);
        return 7;
    }
    if (GetEnvironmentVariableW(L"KPC_EXPORT_CHECK_DELAY_MS", delay, 16)) Sleep(wcstoul(delay, NULL, 10));
    if (!capture(wcstoul(pid, NULL, 10), name, sizeof(name))) { puts("KPC_ERROR The game's memory could not be read."); return 5; }
    wcscpy(loader, output);
    *wcsrchr(loader, L'\\') = 0;
    CreateDirectoryW(loader, NULL);
    file = CreateFileW(output, GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) { puts("KPC_ERROR The export file could not be created."); return 6; }
    snprintf(content, sizeof(content), "{\"format\":\"kpc-character\",\"version\":1,\"name\":\"%s\",\"source\":\"proton-export-check\"}", name);
    WriteFile(file, content, (DWORD)strlen(content), &written, NULL);
    CloseHandle(file);
    printf("KPC_CAPTURED \"%s\"\n", name);
    return 0;
}
