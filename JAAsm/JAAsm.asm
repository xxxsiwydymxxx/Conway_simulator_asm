.code

; MyProc1(byte* src, byte* dst, int rowsize, int start_row, int end_row, int total_height)
MyProc1 PROC
    
    ; RCX = src, RDX = dst, R8 = rowsize, R9 = start_row, [RSP+40] = end_row, [RSP+48] = total_height

    ; push onto stack
    push rbx
    push rsi
    push rdi

    ; allocate stack space for registers
    sub rsp, 160

    ; save registers
    vmovdqu xmmword ptr [rsp],      xmm6
    vmovdqu xmmword ptr [rsp+16],   xmm7
    vmovdqu xmmword ptr [rsp+32],   xmm8
    vmovdqu xmmword ptr [rsp+48],   xmm9
    vmovdqu xmmword ptr [rsp+64],   xmm10
    vmovdqu xmmword ptr [rsp+80],   xmm11
    vmovdqu xmmword ptr [rsp+96],   xmm12
    vmovdqu xmmword ptr [rsp+112],  xmm13
    vmovdqu xmmword ptr [rsp+128],  xmm14
    vmovdqu xmmword ptr [rsp+144],  xmm15

   ; move Rowsize, StartRow, EndRow to 64-bit
    movsxd r8, r8d
    movsxd r9, r9d
    movsxd r10, dword ptr [rsp + 224] ; Load EndRow 224 = 24 + 160 + 40

    mov rax, r9
    imul rax, r8    ; RAX = StartRow * Rowsize offset
    add rcx, rax            ; RCX = Start Src
    add rdx, rax            ; RDX = Start Dst

    sub r10, r9 ; R10 = EndRow - StartRow
    cmp r10, 0  ; If no rows to process, exit
    jle Done

    
    mov eax, 2
    vmovd xmm13, eax ; move 2 into xmm13
    vpbroadcastb ymm13, xmm13 ; 0000 0010b in every byte
    
    mov eax, 3
    vmovd xmm14, eax
    vpbroadcastb ymm14, xmm14 ; 0000 0011b in every byte

    mov eax, 1
    vmovd xmm15, eax
    vpbroadcastb ymm15, xmm15 ; 0000 0001b in every byte

    
RowLoop:
    ; save onto stack
    push rcx
    push rdx

    mov rsi, rcx
    sub rsi, r8             ; Row Above
    mov rdi, rcx            ; Current Row in rcx
    add rdi, r8             ; Row Below

    mov r11, 1              ; x = 1, skip first pixel
    
    
    mov rax, r8
    sub rax, 33 ; rax = rowsize - 33 last time for full 32 byte vector

    
    
VectorLoop:
    cmp r11, rax
    jg CheckRemaining           

    ; above
    vmovdqu ymm0, ymmword ptr [rsi + r11 - 1] 
    vmovdqu ymm1, ymmword ptr [rsi + r11]     
    vmovdqu ymm2, ymmword ptr [rsi + r11 + 1] 
    vpaddb  ymm0, ymm0, ymm1
    vpaddb  ymm0, ymm0, ymm2
    ; middle
    vmovdqu ymm3, ymmword ptr [rcx + r11 - 1] 
    vmovdqu ymm4, ymmword ptr [rcx + r11 + 1] 
    vpaddb  ymm0, ymm0, ymm3
    vpaddb  ymm0, ymm0, ymm4
   ; below
    vmovdqu ymm5, ymmword ptr [rdi + r11 - 1] 
    vmovdqu ymm1, ymmword ptr [rdi + r11]     
    vmovdqu ymm2, ymmword ptr [rdi + r11 + 1] 
    vpaddb  ymm0, ymm0, ymm5
    vpaddb  ymm0, ymm0, ymm1
    vpaddb  ymm0, ymm0, ymm2

    ; Conway rules
    vmovdqu ymm8, ymmword ptr [rcx + r11]     
    vpcmpeqb ymm1, ymm0, ymm13                ; Count == 2
    vpcmpeqb ymm2, ymm0, ymm14                ; Count == 3
    vpand ymm1, ymm1, ymm8                    ; (Count == 2) & Alive
    vpor  ymm1, ymm1, ymm2                    ; OR (Count == 3)
    vpand ymm1, ymm1, ymm15                   ; turn to 0000 0001b
    vmovdqu ymmword ptr [rdx + r11], ymm1     ; Store

    add r11, 32
    jmp VectorLoop

    
CheckRemaining:
   
    
    mov rbx, r8 ; RBX = rowsize
    dec rbx     ; RBX = last pixel we process
    sub rbx, 32     ; RBX = last 32 pixels
    
    cmp r11, rbx   ; if x > last 32 pixels then go to next row
    jg NextRow
   
    mov r11, rbx ; set the loop counter to last 32 pixels
    
    mov rax, rbx ; set to last 32 pixels so it runs once more 
    ; dec rax  ; removed bug
    jmp VectorLoop

NextRow:    
    ; restore from stack
    pop rdx
    pop rcx

    add rcx, r8 ; add rowsize to src
    add rdx, r8 ; add rowsize to dst
  
    dec r10 ; decrement total_height count
    jnz RowLoop ; jmp if rows remain

Done:
    ; restore registers
    vmovdqu xmm6,  xmmword ptr [rsp]
    vmovdqu xmm7,  xmmword ptr [rsp+16]
    vmovdqu xmm8,  xmmword ptr [rsp+32]
    vmovdqu xmm9,  xmmword ptr [rsp+48]
    vmovdqu xmm10, xmmword ptr [rsp+64]
    vmovdqu xmm11, xmmword ptr [rsp+80]
    vmovdqu xmm12, xmmword ptr [rsp+96]
    vmovdqu xmm13, xmmword ptr [rsp+112]
    vmovdqu xmm14, xmmword ptr [rsp+128]
    vmovdqu xmm15, xmmword ptr [rsp+144]

    ; deallocate stack space
    add rsp, 160
    ; restore from stack
    pop rdi
    pop rsi
    pop rbx

    ; return 1
    mov rax, 1
    ret

MyProc1 ENDP
END