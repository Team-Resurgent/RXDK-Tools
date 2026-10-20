#include <cstdio>
#include <stdexcept>
#include <string>
#include <typeinfo>
#include "counter.h"

extern "C" int DbgPrint(const char *fmt, ...);

static int failures;

static void check(bool ok, const char *desc)
{
    DbgPrint("[T] %s %s\n", ok ? "PASS" : "FAIL", desc);
    if (!ok)
        failures++;
}

// thread_local is emulated TLS; in Debug the toolchain recompiles this file with line tables only.
static thread_local int tls_value = 7;

struct Base { virtual ~Base() = default; };
struct Derived : Base {};

int main()
{
    DbgPrint("[T] SECT cmake\n");

    check(counter_next() == 1 && counter_next() == 2, "C static library links");

    bool caught = false;
    try {
        throw std::runtime_error("boom");
    } catch (const std::runtime_error &e) {
        caught = std::string(e.what()) == "boom";
    }
    check(caught, "C++ exception caught");

    Derived d;
    Base &b = d;
    check(typeid(b) == typeid(Derived) && dynamic_cast<Derived *>(&b) != nullptr, "RTTI");

    tls_value += 1;
    check(tls_value == 8, "thread_local");

    FILE *f = std::fopen("D:\\hello.txt", "r");
    char line[64] = {};
    check(f && std::fgets(line, sizeof line, f) && std::string(line) == "hello from the disc\n",
          "deployed file on the disc");
    if (f)
        std::fclose(f);

    f = std::fopen("D:\\media\\sub\\note.txt", "r");
    char nested[16] = {};
    check(f && std::fgets(nested, sizeof nested, f) && std::string(nested) == "nested\n",
          "file from a deployed folder");
    if (f)
        std::fclose(f);

    DbgPrint("[T] DONE cmake %d failures\n", failures);
    DbgPrint("[T] ALLDONE\n");
    return 0;
}
