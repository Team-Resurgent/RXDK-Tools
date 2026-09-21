// SPDX-License-Identifier: GPL-3.0-or-later
// Part of RXDK-Tools - see LICENSE for the full GNU GPL v3.
//
// The DWARF v4/v5 constants we consume. Values per the DWARF spec. Ported
// verbatim from RXDK-360's Rxdk.Xbox360.Dwarf (target-agnostic).

namespace Rxdk.Dwarf;

internal static class DW_TAG
{
    public const int array_type = 0x01;
    public const int class_type = 0x02;
    public const int enumeration_type = 0x04;
    public const int formal_parameter = 0x05;
    public const int member = 0x0d;
    public const int pointer_type = 0x0f;
    public const int compile_unit = 0x11;
    public const int structure_type = 0x13;
    public const int typedef = 0x16;
    public const int union_type = 0x17;
    public const int base_type = 0x24;
    public const int const_type = 0x26;
    public const int subprogram = 0x2e;
    public const int variable = 0x34;
    public const int volatile_type = 0x35;
    public const int restrict_type = 0x37;
    public const int lexical_block = 0x0b;
    public const int subrange_type = 0x21;
    public const int inheritance = 0x1c;
    public const int inlined_subroutine = 0x1d;
    public const int @namespace = 0x39;
}

internal static class DW_AT
{
    public const int name = 0x03;
    public const int byte_size = 0x0b;
    public const int encoding = 0x3e;
    public const int low_pc = 0x11;
    public const int high_pc = 0x12;
    public const int language = 0x13;
    public const int comp_dir = 0x1b;
    public const int stmt_list = 0x10;
    public const int decl_file = 0x3a;
    public const int decl_line = 0x3b;
    public const int type = 0x49;
    public const int location = 0x02;
    public const int frame_base = 0x40;
    public const int external = 0x3f;
    public const int data_member_location = 0x38;
    public const int count = 0x37;
    public const int upper_bound = 0x2f;
    public const int specification = 0x47;
    public const int call_file = 0x58;
    public const int call_line = 0x59;
    public const int abstract_origin = 0x31;
}

internal static class DW_ATE
{
    public const int boolean = 0x02;
    public const int float_ = 0x04;
    public const int signed = 0x05;
    public const int signed_char = 0x06;
    public const int unsigned = 0x07;
    public const int unsigned_char = 0x08;
}

internal static class DW_OP
{
    public const int constu = 0x10;
    public const int consts = 0x11;
    public const int plus_uconst = 0x23;
    public const int lit0 = 0x30;
    public const int lit31 = 0x4f;
}

internal static class DW_FORM
{
    public const int addr = 0x01;
    public const int block2 = 0x03;
    public const int block4 = 0x04;
    public const int data2 = 0x05;
    public const int data4 = 0x06;
    public const int data8 = 0x07;
    public const int @string = 0x08;
    public const int block = 0x09;
    public const int block1 = 0x0a;
    public const int data1 = 0x0b;
    public const int flag = 0x0c;
    public const int sdata = 0x0d;
    public const int strp = 0x0e;
    public const int udata = 0x0f;
    public const int ref_addr = 0x10;
    public const int ref1 = 0x11;
    public const int ref2 = 0x12;
    public const int ref4 = 0x13;
    public const int ref8 = 0x14;
    public const int ref_udata = 0x15;
    public const int indirect = 0x16;
    public const int sec_offset = 0x17;
    public const int exprloc = 0x18;
    public const int flag_present = 0x19;
    public const int data16 = 0x1e;
    public const int line_strp = 0x1f;
    public const int implicit_const = 0x21;
    public const int strx = 0x1a;
    public const int addrx = 0x1b;
    public const int strx1 = 0x25;
    public const int strx2 = 0x26;
    public const int strx3 = 0x27;
    public const int strx4 = 0x28;
    public const int addrx1 = 0x29;
    public const int addrx2 = 0x2a;
    public const int addrx3 = 0x2b;
    public const int addrx4 = 0x2c;
}

// Line-number program opcodes.
internal static class DW_LNS
{
    public const int copy = 1;
    public const int advance_pc = 2;
    public const int advance_line = 3;
    public const int set_file = 4;
    public const int set_column = 5;
    public const int negate_stmt = 6;
    public const int set_basic_block = 7;
    public const int const_add_pc = 8;
    public const int fixed_advance_pc = 9;
    public const int set_prologue_end = 10;
    public const int set_epilogue_begin = 11;
    public const int set_isa = 12;
}

internal static class DW_LNE
{
    public const int end_sequence = 1;
    public const int set_address = 2;
    public const int define_file = 3;
    public const int set_discriminator = 4;
}
