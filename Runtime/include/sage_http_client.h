#ifndef SAGE_HTTP_CLIENT_H
#define SAGE_HTTP_CLIENT_H

#include "sage_text.h"

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

/* Runs curl as a child process WITHOUT a command shell. Each argument is quoted
   with the Windows argv rules, so no caller byte is interpreted as shell syntax.
   Results: >= 0 is curl's exit status (+256 when the response did not fit);
   negative values say why no transfer ran. */
enum
{
    SAGE_HTTP_TRUNCATED = 256,
    SAGE_HTTP_REJECTED = -1,    /* invalid owner, URL, path, or command too long */
    SAGE_HTTP_UNAVAILABLE = -2, /* curl could not be started, or a pipe failed */
    SAGE_HTTP_UNSUPPORTED = -3  /* no process backend on this platform */
};

#define SAGE_HTTP_COMMAND_LIMIT 32767
#define SAGE_HTTP_URL_LIMIT 8192

/* http:// or https:// followed by visible ASCII only (no spaces or controls). */
static int sage_http_valid_url(const SageTextState* url)
{
    if (!url || url->length > SAGE_HTTP_URL_LIMIT) return 0;
    size_t prefix = 0;
    if (url->length > 7 && memcmp(url->data, "http://", 7) == 0) prefix = 7;
    else if (url->length > 8 && memcmp(url->data, "https://", 8) == 0) prefix = 8;
    if (prefix == 0) return 0;
    for (size_t index = 0; index < url->length; ++index)
        if (url->data[index] < 0x21 || url->data[index] > 0x7E) return 0;
    return 1;
}

/* Printable ASCII (spaces allowed); must not look like a curl option. */
static int sage_http_valid_path(const SageTextState* path)
{
    if (!path || path->length == 0 || path->length > 259 || path->data[0] == '-') return 0;
    for (size_t index = 0; index < path->length; ++index)
        if (path->data[index] < 0x20 || path->data[index] > 0x7E) return 0;
    return 1;
}

typedef struct SageHttpCommand
{
    char text[SAGE_HTTP_COMMAND_LIMIT + 1];
    size_t length;
    int overflow;
} SageHttpCommand;

static void sage_http_put(SageHttpCommand* command, char value)
{
    if (command->length >= SAGE_HTTP_COMMAND_LIMIT) { command->overflow = 1; return; }
    command->text[command->length++] = value;
    command->text[command->length] = '\0';
}

/* CommandLineToArgvW / MSVCRT quoting: backslashes double only before a quote. */
static void sage_http_put_argument(SageHttpCommand* command, const unsigned char* bytes, size_t length)
{
    if (command->length > 0) sage_http_put(command, ' ');
    sage_http_put(command, '"');
    size_t backslashes = 0;
    for (size_t index = 0; index < length; ++index)
    {
        unsigned char value = bytes[index];
        if (value == '\\') { ++backslashes; continue; }
        size_t count = value == '"' ? backslashes * 2 + 1 : backslashes;
        for (size_t slash = 0; slash < count; ++slash) sage_http_put(command, '\\');
        sage_http_put(command, (char)value);
        backslashes = 0;
    }
    for (size_t slash = 0; slash < backslashes * 2; ++slash) sage_http_put(command, '\\');
    sage_http_put(command, '"');
}

static void sage_http_put_literal(SageHttpCommand* command, const char* value)
{
    sage_http_put_argument(command, (const unsigned char*)value, strlen(value));
}

/* -q skips .curlrc; --proto limits every transfer and redirect to HTTP(S). */
static void sage_http_begin(SageHttpCommand* command)
{
    command->length = 0;
    command->overflow = 0;
    command->text[0] = '\0';
    sage_http_put_literal(command, "curl");
    sage_http_put_literal(command, "-q");
    sage_http_put_literal(command, "--silent");
    sage_http_put_literal(command, "--proto");
    sage_http_put_literal(command, "=http,https");
    sage_http_put_literal(command, "--proto-redir");
    sage_http_put_literal(command, "=http,https");
}

#ifdef _WIN32
/* Starts the command, optionally feeding input bytes to its stdin, and captures
   stdout into response (replacing its contents; overflow is drained and flagged). */
static int sage_http_run(SageHttpCommand* command, const SageTextState* input, SageTextState* response)
{
    if (command->overflow) return SAGE_HTTP_REJECTED;
    SECURITY_ATTRIBUTES inherit = { sizeof(inherit), NULL, TRUE };
    HANDLE out_read = NULL, out_write = NULL, in_read = NULL, in_write = NULL;
    int status = SAGE_HTTP_UNAVAILABLE;
    if (response)
    {
        if (!CreatePipe(&out_read, &out_write, &inherit, 0)) goto done;
        if (!SetHandleInformation(out_read, HANDLE_FLAG_INHERIT, 0)) goto done;
    }
    if (input)
    {
        if (!CreatePipe(&in_read, &in_write, &inherit, 0)) goto done;
        if (!SetHandleInformation(in_write, HANDLE_FLAG_INHERIT, 0)) goto done;
    }
    STARTUPINFOA startup;
    PROCESS_INFORMATION process;
    ZeroMemory(&startup, sizeof(startup));
    ZeroMemory(&process, sizeof(process));
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = in_read;
    startup.hStdOutput = out_write;
    startup.hStdError = NULL;
    /* No application name: Windows searches for curl.exe (application directory,
       current directory, system directories, then PATH). No shell is involved. */
    if (!CreateProcessA(NULL, command->text, NULL, NULL, TRUE, CREATE_NO_WINDOW, NULL, NULL, &startup, &process)) goto done;
    CloseHandle(process.hThread);
    if (out_write) { CloseHandle(out_write); out_write = NULL; }
    if (in_read) { CloseHandle(in_read); in_read = NULL; }

    if (in_write)
    {
        /* curl reads all of "@-" before transferring, so writing first cannot deadlock. */
        size_t sent = 0;
        while (sent < input->length)
        {
            DWORD chunk = input->length - sent > 65536 ? 65536 : (DWORD)(input->length - sent);
            DWORD written = 0;
            if (!WriteFile(in_write, input->data + sent, chunk, &written, NULL) || written == 0) break;
            sent += written;
        }
        CloseHandle(in_write);
        in_write = NULL;
    }
    int truncated = 0;
    if (out_read)
    {
        response->length = 0;
        unsigned char chunk[4096];
        DWORD received = 0;
        while (ReadFile(out_read, chunk, sizeof(chunk), &received, NULL) && received > 0)
        {
            size_t room = response->capacity - response->length;
            size_t keep = received < room ? received : room;
            memcpy(response->data + response->length, chunk, keep);
            response->length += keep;
            if (keep < received) truncated = 1;
        }
    }
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exit_code = 0;
    if (!GetExitCodeProcess(process.hProcess, &exit_code)) exit_code = 255;
    CloseHandle(process.hProcess);
    status = (int)(exit_code > 255 ? 255 : exit_code) + (truncated ? SAGE_HTTP_TRUNCATED : 0);
done:
    if (out_read) CloseHandle(out_read);
    if (out_write) CloseHandle(out_write);
    if (in_read) CloseHandle(in_read);
    if (in_write) CloseHandle(in_write);
    return status;
}
#else
static int sage_http_run(SageHttpCommand* command, const SageTextState* input, SageTextState* response)
{
    (void)command; (void)input; (void)response;
    return SAGE_HTTP_UNSUPPORTED;
}
#endif

/* The command buffer is large; keep it off the stack. */
static int sage_http_execute(int (*build)(SageHttpCommand*, const void*, const void*), const void* first, const void* second,
    const SageTextState* input, SageTextState* response)
{
    SageHttpCommand* command = malloc(sizeof(*command));
    if (!command) return SAGE_HTTP_UNAVAILABLE;
    int status = build(command, first, second) ? sage_http_run(command, input, response) : SAGE_HTTP_REJECTED;
    free(command);
    return status;
}

static int sage_http_build_get(SageHttpCommand* command, const void* url, const void* unused)
{
    (void)unused;
    sage_http_begin(command);
    sage_http_put_argument(command, ((const SageTextState*)url)->data, ((const SageTextState*)url)->length);
    return 1;
}

static int sage_http_build_post(SageHttpCommand* command, const void* url, const void* unused)
{
    (void)unused;
    sage_http_begin(command);
    sage_http_put_literal(command, "--data-binary");
    sage_http_put_literal(command, "@-");
    sage_http_put_argument(command, ((const SageTextState*)url)->data, ((const SageTextState*)url)->length);
    return 1;
}

static int sage_http_build_download(SageHttpCommand* command, const void* url, const void* path)
{
    sage_http_begin(command);
    sage_http_put_literal(command, "--output");
    sage_http_put_argument(command, ((const SageTextState*)path)->data, ((const SageTextState*)path)->length);
    sage_http_put_argument(command, ((const SageTextState*)url)->data, ((const SageTextState*)url)->length);
    return 1;
}

static int sage_http_get(const SageTextState* url, SageTextState* response)
{
    if (!response || !sage_http_valid_url(url)) return SAGE_HTTP_REJECTED;
    return sage_http_execute(sage_http_build_get, url, NULL, NULL, response);
}

static int sage_http_post(const SageTextState* url, const SageTextState* data, SageTextState* response)
{
    if (!response || !data || !sage_http_valid_url(url)) return SAGE_HTTP_REJECTED;
    return sage_http_execute(sage_http_build_post, url, NULL, data, response);
}

static int sage_http_download(const SageTextState* url, const SageTextState* path)
{
    if (!sage_http_valid_url(url) || !sage_http_valid_path(path)) return SAGE_HTTP_REJECTED;
    return sage_http_execute(sage_http_build_download, url, path, NULL, NULL);
}

#endif
