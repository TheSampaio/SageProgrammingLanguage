#pragma once
#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>

/* Native test-only instrumentation; never distributed as the Sage runtime. */
static void* ownership_live[128];
static int ownership_count;
static int ownership_order;

static void* ownership_allocate(size_t size)
{
    void* value = malloc(size);
    if (!value || ownership_count == 128) exit(3);
    ownership_live[ownership_count++] = value;
    return value;
}

static void ownership_release(void* value)
{
    if (!value) return;
    for (int i = 0; i < ownership_count; ++i) {
        if (ownership_live[i] != value) continue;
        ownership_order = ownership_order * 10 + *(int32_t*)value;
        ownership_live[i] = ownership_live[--ownership_count];
        free(value);
        return;
    }
    puts("[FAIL] invalid_or_duplicate_free");
    exit(2);
}

static int ownership_outstanding(void) { return ownership_count; }
static int ownership_release_order(void) { return ownership_order; }
static void ownership_reset_order(void) { ownership_order = 0; }
static void ownership_check(bool condition, char* name)
{
    printf("[%s] %s\n", condition ? "PASS" : "FAIL", name);
}

#define malloc ownership_allocate
#define free ownership_release
