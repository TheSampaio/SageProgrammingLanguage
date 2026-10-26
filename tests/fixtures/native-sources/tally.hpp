#pragma once
namespace lib {
    // Declared here, defined in tally.cpp: the library is not header-only.
    class Tally {
    public:
        explicit Tally(int start);
        ~Tally();
        int add(int amount);
    private:
        int total_;
    };
    int square(int value);
}
