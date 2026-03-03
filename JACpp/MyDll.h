#pragma once


extern "C" __declspec(dllexport)
int computeGeneration(unsigned char* src, unsigned char* dst, int rowsize, int start_row, int end_row, int total_height);