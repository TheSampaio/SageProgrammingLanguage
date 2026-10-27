#include <stdio.h>
#include <string.h>
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif

/* Test double for curl.exe: prints each received argument in brackets so a test
   can check exact argv transfer, echoes stdin after "@-", and writes the argument
   list to the "--output" path instead of stdout. */
int main(int argc, char** argv)
{
#ifdef _WIN32
    _setmode(_fileno(stdout), _O_BINARY); /* emit bytes like the real curl */
#endif
    FILE* out = stdout;
    int read_input = 0;
    for (int index = 1; index + 1 < argc; ++index)
        if (strcmp(argv[index], "--output") == 0 && !(out = fopen(argv[index + 1], "w"))) return 9;
    for (int index = 1; index < argc; ++index)
    {
        fprintf(out, "<%s>\n", argv[index]);
        if (strcmp(argv[index], "@-") == 0) read_input = 1;
    }
    if (read_input)
    {
        int value;
        fprintf(out, "stdin:");
        while ((value = getchar()) != EOF) fputc(value, out);
        fprintf(out, "\n");
    }
    if (out != stdout && fclose(out) != 0) return 10;
    return argc > 1 && strstr(argv[argc - 1], "exit7") ? 7 : 0;
}
