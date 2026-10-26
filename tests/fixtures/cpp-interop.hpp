#pragma once
#include <stdexcept>
#include <cstdio>
namespace demo {
    inline int add(int a, int b) { return a + b; }
    inline double add(double a, double b) { return a + b; }
    template <typename T> T biggest(T a, T b) { return a > b ? a : b; }
    inline void report(int a, double b, int c) { std::printf("sum=%d real=%.1f top=%d\n", a, b, c); }
    inline int fail(int code) { if (code != 0) throw std::runtime_error("bad code"); return 0; }
    class Counter {
    public:
        explicit Counter(int start) : value_(start) {}
        ~Counter() { std::printf("[counter dropped %d]\n", value_); }
        int increment(int by) { value_ += by; return value_; }
        int value() const { return value_; }
    private:
        int value_;
    };
}
