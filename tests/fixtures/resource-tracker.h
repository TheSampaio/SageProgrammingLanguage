#pragma once
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>

/* Native test fixture: catches duplicate/invalid release without dereferencing it. */
typedef struct ResourceHandle { int id; } ResourceHandle;
static ResourceHandle* resource_live[128];
static int resource_count;
static int resource_order;
static int resource_calls;
static int resource_status;

static ResourceHandle* resource_open(int id)
{
    ResourceHandle* handle = malloc(sizeof(*handle));
    if (!handle || resource_count == 128) exit(3);
    handle->id = id;
    resource_live[resource_count++] = handle;
    return handle;
}
static ResourceHandle* resource_null(void) { return NULL; }
static int resource_close(ResourceHandle* handle)
{
    ++resource_calls;
    for (int i = 0; i < resource_count; ++i) {
        if (resource_live[i] != handle) continue;
        resource_order = resource_order * 10 + handle->id;
        resource_live[i] = resource_live[--resource_count];
        free(handle);
        return resource_status;
    }
    puts("[FAIL] invalid_or_duplicate_resource_drop");
    exit(2);
}
static void resource_close_void(ResourceHandle* handle) { (void)resource_close(handle); }
static int resource_read(ResourceHandle* handle) { return handle->id; }
static void resource_write(ResourceHandle* handle, int id) { handle->id = id; }
static int resource_outstanding(void) { return resource_count; }
static int resource_release_order(void) { return resource_order; }
static int resource_drop_calls(void) { return resource_calls; }
static void resource_reset(void) { resource_order = 0; resource_calls = 0; resource_status = 0; }
static void resource_fail_close(void) { resource_status = -1; }
static void resource_check(bool condition, char* name)
{
    printf("[%s] %s\n", condition ? "PASS" : "FAIL", name);
}
