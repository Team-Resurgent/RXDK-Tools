#include "counter.h"

/* C, built into a static library by CMake's own archive rule. */
static int value;

int counter_next(void)
{
    return ++value;
}
