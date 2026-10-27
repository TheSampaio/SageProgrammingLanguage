#ifndef SAGE_HTTP_SERVER_H
#define SAGE_HTTP_SERVER_H

#include "sage_text.h"

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <winsock2.h>

/* Owned listening socket plus at most one pending client connection. Drop closes
   both sockets and balances WSAStartup, on every path. */
typedef struct SageHttpListenerState
{
    SOCKET listener;
    SOCKET client;
    unsigned short port;
} SageHttpListenerState;

#define SAGE_HTTP_REQUEST_LIMIT 8192
#define SAGE_HTTP_RECEIVE_TIMEOUT_MS 5000

static SageHttpListenerState* sage_http_listen(unsigned short port, int loopback_only)
{
    WSADATA data;
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) return NULL;
    SOCKET listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == INVALID_SOCKET) { WSACleanup(); return NULL; }
    BOOL exclusive = TRUE; /* no other socket may bind the same port meanwhile */
    struct sockaddr_in address;
    ZeroMemory(&address, sizeof(address));
    address.sin_family = AF_INET;
    address.sin_port = htons(port);
    address.sin_addr.s_addr = htonl(loopback_only ? INADDR_LOOPBACK : INADDR_ANY);
    int length = sizeof(address);
    SageHttpListenerState* state = NULL;
    if (setsockopt(listener, SOL_SOCKET, SO_EXCLUSIVEADDRUSE, (const char*)&exclusive, sizeof(exclusive)) == SOCKET_ERROR ||
        bind(listener, (struct sockaddr*)&address, sizeof(address)) == SOCKET_ERROR ||
        listen(listener, SOMAXCONN) == SOCKET_ERROR ||
        getsockname(listener, (struct sockaddr*)&address, &length) == SOCKET_ERROR ||
        !(state = malloc(sizeof(*state))))
    {
        closesocket(listener);
        WSACleanup();
        return NULL;
    }
    state->listener = listener;
    state->client = INVALID_SOCKET;
    state->port = ntohs(address.sin_port);
    return state;
}

static unsigned short sage_http_listener_port(const SageHttpListenerState* state)
{
    return state ? state->port : 0;
}

static void sage_http_close_client(SageHttpListenerState* state)
{
    if (state->client == INVALID_SOCKET) return;
    shutdown(state->client, SD_SEND);
    closesocket(state->client);
    state->client = INVALID_SOCKET;
}

/* Blocks for one connection and replaces request with its head (request line and
   headers, up to the blank line), bounded by capacity and SAGE_HTTP_REQUEST_LIMIT.
   A previous unanswered client is closed first. Returns 1 when a client is pending. */
static int sage_http_accept(SageHttpListenerState* state, SageTextState* request)
{
    if (!state || !request) return 0;
    sage_http_close_client(state);
    request->length = 0;
    SOCKET client = accept(state->listener, NULL, NULL);
    if (client == INVALID_SOCKET) return 0;
    state->client = client;
    DWORD timeout = SAGE_HTTP_RECEIVE_TIMEOUT_MS;
    setsockopt(client, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
    unsigned char head[SAGE_HTTP_REQUEST_LIMIT];
    size_t total = 0;
    while (total < sizeof(head))
    {
        int received = recv(client, (char*)head + total, (int)(sizeof(head) - total), 0);
        if (received <= 0) break;
        total += (size_t)received;
        int complete = 0;
        for (size_t index = 3; index < total && !complete; ++index)
            complete = memcmp(head + index - 3, "\r\n\r\n", 4) == 0;
        if (complete) break;
    }
    size_t end = total;
    for (size_t index = 3; index < total; ++index)
        if (memcmp(head + index - 3, "\r\n\r\n", 4) == 0) { end = index - 3; break; }
    size_t keep = end < request->capacity ? end : request->capacity;
    memcpy(request->data, head, keep);
    request->length = keep;
    return 1;
}

static int sage_http_send_all(SOCKET client, const char* bytes, size_t length)
{
    while (length > 0)
    {
        int chunk = length > 65536 ? 65536 : (int)length;
        int sent = send(client, bytes, chunk, 0);
        if (sent <= 0) return 0;
        bytes += sent;
        length -= (size_t)sent;
    }
    return 1;
}

static const char* sage_http_reason(unsigned int status)
{
    switch (status)
    {
        case 200: return "OK";
        case 201: return "Created";
        case 204: return "No Content";
        case 400: return "Bad Request";
        case 404: return "Not Found";
        case 405: return "Method Not Allowed";
        case 500: return "Internal Server Error";
        default: return "Status";
    }
}

/* Sends a complete text/plain response to the pending client and closes it.
   Returns 1 only when every byte was sent. Status must be 100..599. */
static int sage_http_respond(SageHttpListenerState* state, unsigned int status, const SageTextState* body)
{
    if (!state || state->client == INVALID_SOCKET) return 0;
    int sent = 0;
    if (status >= 100 && status <= 599)
    {
        char header[160];
        size_t length = sage_text_length(body);
        int header_length = snprintf(header, sizeof(header),
            "HTTP/1.1 %u %s\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: %lu\r\nConnection: close\r\n\r\n",
            status, sage_http_reason(status), (unsigned long)length);
        sent = header_length > 0 && (size_t)header_length < sizeof(header) &&
            sage_http_send_all(state->client, header, (size_t)header_length) &&
            (length == 0 || sage_http_send_all(state->client, (const char*)body->data, length));
    }
    sage_http_close_client(state);
    return sent;
}

static void sage_http_listener_drop(SageHttpListenerState* state)
{
    if (!state) return;
    sage_http_close_client(state);
    closesocket(state->listener);
    WSACleanup();
    free(state);
}
#else
typedef struct SageHttpListenerState { int unsupported; } SageHttpListenerState;
static SageHttpListenerState* sage_http_listen(unsigned short port, int loopback_only) { (void)port; (void)loopback_only; return NULL; }
static unsigned short sage_http_listener_port(const SageHttpListenerState* state) { (void)state; return 0; }
static int sage_http_accept(SageHttpListenerState* state, SageTextState* request) { (void)state; (void)request; return 0; }
static int sage_http_respond(SageHttpListenerState* state, unsigned int status, const SageTextState* body) { (void)state; (void)status; (void)body; return 0; }
static void sage_http_listener_drop(SageHttpListenerState* state) { free(state); }
#endif

#endif
