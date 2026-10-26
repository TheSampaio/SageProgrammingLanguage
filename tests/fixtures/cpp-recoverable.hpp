#pragma once
#include <cstdio>
#include <stdexcept>
namespace safe {
    inline int divide(int a, int b) { if (b == 0) throw std::invalid_argument("division by zero"); return a / b; }
    inline void check(int value) { if (value < 0) throw 42; }
    class Gate {
    public:
        explicit Gate(int limit) : limit_(limit) { if (limit < 0) throw std::range_error("negative limit"); }
        ~Gate() { std::printf("[gate dropped %d]\n", limit_); }
        int take(int amount) { if (amount > limit_) throw std::runtime_error("over limit"); limit_ -= amount; return limit_; }
    private:
        int limit_;
    };
}
