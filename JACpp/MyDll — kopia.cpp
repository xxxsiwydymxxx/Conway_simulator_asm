#include "MyDll.h"
#include <iostream>
#include <thread>
#include <chrono>
#include <cstring> // Required for memset and memcpy

#define ARRAY_SIZE 12000

// Helper to set the bit in the destination array
void setbitifalive(unsigned char* dest_row, int byte_index, int alive_neighbours, bool is_currently_alive, int bit_index) {
    bool alive = false;

    if (is_currently_alive) {
        if (alive_neighbours == 2) {
            alive = true;
        }
        else if (alive_neighbours == 3) {
			alive = true;
        }

    }
    else {
        if (alive_neighbours == 3) {
            alive = true;
        }
    }

    if (alive) {
		int shifts = 7 - bit_index;
        unsigned char pixel_mask = 1 << shifts;

		unsigned char current_byte = dest_row[byte_index];
        
		unsigned char new_byte = current_byte | pixel_mask;

		dest_row[byte_index] = new_byte;
    }
}

// Helper to safely get a bit value from a row
// Returns 0 if the byte_index is out of bounds or if the row pointer is null
inline int get_bit_safe(unsigned char* row, int byte_index, int bit_index, int row_width) {
    if (row == nullptr) {
        return 0;
    }
    
    if (byte_index < 0 or byte_index >= row_width){
        return 0;
    }
    
	int right_shift_amount = 7 - bit_index;
	unsigned char shifted_byte = row[byte_index] >> right_shift_amount;
    
    if ((shifted_byte & 1) == 1) {
        return 1;
    }
    else{
        return 0;
    }
}

extern "C" __declspec(dllexport)
int computeGeneration(unsigned char* bytes, int length, int rowsize)
{
    if (!bytes) {
        return -1;
    }
    if (length <= 0 or rowsize <= 0) {
		return -1;
    }
       
    unsigned char result[ARRAY_SIZE];
    std::memset(result, 0, ARRAY_SIZE); // Clear result buffer

    int height = length / rowsize;

    for (int y = 0; y < height; ++y)
    {
        // Define pointers for current, previous, and next rows
        // If y is at the edge, the prev or next pointers will be NULL
        unsigned char* current_row = bytes + y * rowsize;
        unsigned char* prev_row = (y > 0) ? bytes + (y - 1) * rowsize : nullptr;
        unsigned char* next_row = (y < height - 1) ? bytes + (y + 1) * rowsize : nullptr;

        unsigned char* dest_row = result + y * rowsize;

        for (int i = 0; i < rowsize; i++) {
            for (int bit = 0; bit < 8; bit++) {

                int alive_neighbours = 0;

                // 1. Determine the Byte and Bit coordinates for Left and Right neighbors
                // This handles the transition between bytes (e.g. bit 0's left neighbor is bit 7 of the previous byte)

                // Left Coords
                int left_byte_idx = i;
                int left_bit_idx = bit - 1;
                if (left_bit_idx < 0) {
                    left_byte_idx--;
                    left_bit_idx = 7;
                }

                // Right Coords
                int right_byte_idx = i;
                int right_bit_idx = bit + 1;
                if (right_bit_idx > 7) {
                    right_byte_idx++;
                    right_bit_idx = 0;
                }

                // 2. Sum neighbors from Previous Row (Top-Left, Top, Top-Right)
                alive_neighbours += get_bit_safe(prev_row, left_byte_idx, left_bit_idx, rowsize);  // Top-Left
                alive_neighbours += get_bit_safe(prev_row, i, bit, rowsize);                       // Top
                alive_neighbours += get_bit_safe(prev_row, right_byte_idx, right_bit_idx, rowsize); // Top-Right

                // 3. Sum neighbors from Current Row (Left, Right)
                alive_neighbours += get_bit_safe(current_row, left_byte_idx, left_bit_idx, rowsize);   // Left
                alive_neighbours += get_bit_safe(current_row, right_byte_idx, right_bit_idx, rowsize); // Right

                // 4. Sum neighbors from Next Row (Bottom-Left, Bottom, Bottom-Right)
                alive_neighbours += get_bit_safe(next_row, left_byte_idx, left_bit_idx, rowsize);  // Bottom-Left
                alive_neighbours += get_bit_safe(next_row, i, bit, rowsize);                       // Bottom
                alive_neighbours += get_bit_safe(next_row, right_byte_idx, right_bit_idx, rowsize); // Bottom-Right

                // 5. Determine current cell state and update result
                bool currentlyAlive = (current_row[i] >> (7 - bit)) & 1;
                setbitifalive(dest_row, i, alive_neighbours, currentlyAlive, bit);
            }
        }
    }

    // Copy the result back to the original buffer
    std::memcpy(bytes, result, ARRAY_SIZE);

    return 1; // success
}