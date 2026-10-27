#include "tally.hpp"
#include <cstdio>
#include <vector>

namespace lib {
    Tally::Tally(int start) : total_(start) {}
    Tally::~Tally() { std::printf("[tally dropped %d]\n", total_); }
    int Tally::add(int amount) { std::vector<int> history{total_, amount}; total_ = history[0] + history[1]; return total_; }
    int square(int value) { return value * value; }
}
