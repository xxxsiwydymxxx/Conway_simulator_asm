#include "MyDll.h"
#include <cstring> 

extern "C" __declspec(dllexport)
int computeGeneration(unsigned char* src, unsigned char* dst, int rowsize, int start_row, int end_row, int total_height)
{
    if (!src || !dst)
    {
        return -1;
    }

    // Iterate through the assigned rows
    // The C# code ensures start_row is at least 1 and end_row is at most total_height - 1
    for (int y = start_row; y < end_row; ++y)
    {
        // Calculate pointers for the row above current row and row below
        unsigned char* row_prev = src + (y - 1) * rowsize;
        unsigned char* row_curr = src + y * rowsize;
        unsigned char* row_next = src + (y + 1) * rowsize;

        unsigned char* row_dst = dst + y * rowsize;

        // Iterate columns skipping the first and last column to avoid boundary checks
        for (int x = 1; x < rowsize - 1; x++)
        {
            int neighbors = 0;

            // 1. Sum Neighbors from Previous Row
            neighbors += row_prev[x - 1];
            neighbors += row_prev[x];
            neighbors += row_prev[x + 1];

            // 2. Sum Neighbors from Current Row
            neighbors += row_curr[x - 1];
            neighbors += row_curr[x + 1];

            // 3. Sum Neighbors from Next Row
            neighbors += row_next[x - 1];
            neighbors += row_next[x];
            neighbors += row_next[x + 1];

            // 4. Apply Game of Life Rules
            unsigned char current = row_curr[x];

            if (current == 1)
            {
                // Alive cell stays alive if it has 2 or 3 neighbors
                if (neighbors == 2 || neighbors == 3)
                {
                    row_dst[x] = 1;
                }
                else
                {
                    row_dst[x] = 0;
                }
            }
            else
            {
                // Dead cell becomes alive if it has exactly 3 neighbors
                if (neighbors == 3)
                {
                    row_dst[x] = 1;
                }
                else
                {
                    row_dst[x] = 0;
                }
            }
        }
    }

    return 1;
}