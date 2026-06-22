namespace Rxdk.XboxDbgBridge.Symbols;

using Rxdk.XboxDbgBridge.Interop;

internal sealed partial class SymbolTypeEngine
{
    private const uint MapNodeOffLeft = 0;
    private const uint MapNodeOffParent = 4;
    private const uint MapNodeOffRight = 8;
    private const uint MapNodeOffIsNil = 13;
    private const uint MapNodeOffMyval = 16;
    private const int MapTreeMaxSteps = 64;
    private const uint VtR4 = 10;

    private bool EmitStdVectorMembers(uint typeId, nuint baseAddr, uint objSize, KitMemoryAccess memory, VariableJson variables)
    {
        if (!TryReadVectorLayout(typeId, baseAddr, objSize, memory, out var first, out _, out var elemSize, out var count))
            return false;

        variables.Append("size", count.ToString());
        for (uint i = 0; i < count && !variables.IsFull; i++)
        {
            var dword = memory.ReadDword((nuint)(first + i * elemSize));
            if (dword is null)
                continue;
            variables.Append($"[{i}]", FormatScalar(dword.Value, $"[{i}]", 0));
        }

        return variables.Count > 0;
    }

    private bool EmitStdMapMembers(uint typeId, nuint baseAddr, uint objSize, KitMemoryAccess memory, VariableJson variables)
    {
        if (!TryReadMapLayout(typeId, baseAddr, objSize, memory, out var myhead, out var size))
            return false;

        variables.Append("size", size.ToString());
        if (size == 0 || myhead == 0)
            return variables.Count > 0;

        if (!MapTreeMinimum(myhead, memory, out var node) || node == 0 || node == myhead)
            return variables.Count > 0;

        uint emitted = 0;
        const uint keySize = 4;
        do
        {
            if (emitted >= size || emitted >= 256 || variables.IsFull)
                break;

            var key = memory.ReadDword((nuint)(node + MapNodeOffMyval));
            var val = memory.ReadDword((nuint)(node + MapNodeOffMyval + keySize));
            if (key is null || val is null)
                break;

            variables.Append($"[{key.Value}]", FormatScalar(val.Value, $"[{key.Value}]", 0));
            emitted++;
            if (!MapTreeSuccessor(myhead, node, memory, out node) || node == 0 || node == myhead)
                break;
        } while (true);

        return variables.Count > 0;
    }

    private bool TryReadVectorLayout(
        uint typeId,
        nuint baseAddr,
        uint objSize,
        KitMemoryAccess memory,
        out uint first,
        out uint last,
        out uint elemSize,
        out uint count)
    {
        first = 0;
        last = 0;
        elemSize = 0;
        count = 0;

        typeId = GetUdtTypeIndex(typeId);
        TryGetTypeName(typeId, out var typeName);
        var likelyVector = TypeNameLooksLikeVector(typeName);
        if (!likelyVector && objSize == 12 && ResolveArrayTypeId(typeId) == 0)
            likelyVector = true;
        if (!likelyVector)
            return false;

        if (!ResolveVectorPointerOffsets(typeId, objSize, out var offFirst, out var offLast))
            return false;

        var firstPtr = memory.ReadDword(baseAddr + offFirst);
        var lastPtr = memory.ReadDword(baseAddr + offLast);
        if (firstPtr is null || lastPtr is null)
            return false;

        first = firstPtr.Value;
        last = lastPtr.Value;
        elemSize = GuessVectorElemSize(typeName);
        if (elemSize == 0)
            return false;

        if (!VectorCountFromPointers(first, last, elemSize, out count))
            return false;
        return true;
    }

    private bool TryReadMapLayout(uint typeId, nuint baseAddr, uint objSize, KitMemoryAccess memory, out uint head, out uint size)
    {
        head = 0;
        size = 0;
        typeId = GetUdtTypeIndex(typeId);
        TryGetTypeName(typeId, out var typeName);
        var likelyMap = TypeNameLooksLikeMap(typeName);
        if (!likelyMap && !TryFindMemberOffset(typeId, "_Myhead", out _))
            return false;

        if (!ResolveMapPointerOffsets(typeId, objSize, likelyMap, out var offHead, out var offSize))
            return false;

        var headPtr = memory.ReadDword(baseAddr + offHead);
        var sizeVal = memory.ReadDword(baseAddr + offSize);
        if (headPtr is null || sizeVal is null)
            return false;

        if (sizeVal.Value > 256)
            return false;

        head = headPtr.Value;
        size = sizeVal.Value;
        return true;
    }

    private string FormatAggregateSummary(uint typeId, nuint addr, uint objSize, KitMemoryAccess memory)
    {
        if (TryReadArrayLayout(typeId, out var elemCount, out _))
            return $"array[{elemCount}]";
        if (TryReadMapLayout(typeId, addr, objSize, memory, out _, out var mapSize))
            return $"map size={mapSize}";
        if (TryReadVectorLayout(typeId, addr, objSize, memory, out _, out _, out _, out var vecCount))
            return $"vector size={vecCount}";
        if (objSize >= 4 && objSize % 4 == 0 && objSize <= 1024)
        {
            TryGetTypeName(GetUdtTypeIndex(typeId), out var typeName);
            if (!TypeNameLooksLikeMap(typeName) && !TypeNameLooksLikeVector(typeName))
                return $"array[{objSize / 4}]";
        }

        return $"{{{objSize} bytes}}";
    }

    private bool TypeLooksLikeStdAggregate(uint typeId, uint objSize)
    {
        if (ResolveArrayTypeId(typeId) != 0)
            return true;
        TryGetTypeName(GetUdtTypeIndex(typeId), out var typeName);
        if (TypeNameLooksLikeVector(typeName) || TypeNameLooksLikeMap(typeName))
            return true;
        return objSize == 8 && TypeNameLooksLikeMap(typeName) ||
               objSize == 12 && TypeNameLooksLikeVector(typeName);
    }

    private bool IsFloatTypeId(uint typeId)
    {
        return TryGetTypeDword(typeId, DbgHelpNative.TiGetBaseType, out var baseType) && baseType == VtR4;
    }

    private static bool IsFloatMemberName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name is "x" or "y" or "z" or "w")
            return true;
        if (name.Length >= 2 && name[0] == 'f' && (name[1] == 'S' || name[1] == 'E'))
            return true;
        if (name.Length >= 2 && name[0] == 'f' && name[1] >= '0' && name[1] <= '9')
            return true;
        if (name.Length >= 2 && name[0] == '_' && name[1] >= '0' && name[1] <= '9')
            return true;
        return false;
    }

    private string FormatScalar(uint dword, string name, uint fieldTypeId)
    {
        if (IsFloatTypeId(fieldTypeId) || IsFloatMemberName(name))
        {
            var bits = BitConverter.Int32BitsToSingle((int)dword);
            return $"{bits:g} (0x{dword:x8})";
        }

        if (name.StartsWith("g_", StringComparison.Ordinal) || (name.StartsWith('p') && name.Length > 1))
            return $"0x{dword:x8}";
        return $"{(int)dword} (0x{dword:x8})";
    }

    private static bool TypeNameLooksLikeVector(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;
        var index = typeName.IndexOf("vector", StringComparison.Ordinal);
        if (index < 0)
            return false;
        if (index > 0 && (typeName[index - 1] is ':' or '>'))
            return true;
        return index == 0 || typeName[index - 1] is ' ' or '<' ||
               typeName.Contains("vector<", StringComparison.Ordinal) ||
               typeName.Contains("vector>", StringComparison.Ordinal);
    }

    private static bool TypeNameLooksLikeMap(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;
        var index = typeName.IndexOf("map", StringComparison.Ordinal);
        if (index < 0)
            return false;
        if (index > 0 && (typeName[index - 1] is ':' or '>'))
            return true;
        return index == 0 || typeName[index - 1] is ' ' or '<' ||
               typeName.Contains("map<", StringComparison.Ordinal) ||
               typeName.Contains("map>", StringComparison.Ordinal);
    }

    private static uint GuessVectorElemSize(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return 4;
        var index = typeName.IndexOf("vector<", StringComparison.Ordinal);
        if (index < 0)
            index = typeName.IndexOf("vector", StringComparison.Ordinal);
        if (index < 0)
            return 4;

        var element = typeName.AsSpan(index);
        if (element.StartsWith("vector<"))
            element = element[7..];
        else
            element = element[6..];

        while (element.Length > 0 && element[0] == ' ')
            element = element[1..];

        if (element.StartsWith("unsigned int"))
            return 4;
        if (element.StartsWith("int") && (element.Length == 3 || element[3] is ',' or '>'))
            return 4;
        if (element.StartsWith("long"))
            return 4;
        if (element.StartsWith("float"))
            return 4;
        if (element.StartsWith("double"))
            return 8;
        if (element.StartsWith("char") && (element.Length == 4 || element[4] is ',' or '>'))
            return 1;
        return 4;
    }

    private bool ResolveVectorPointerOffsets(uint typeId, uint objSize, out uint offFirst, out uint offLast)
    {
        offFirst = 0;
        offLast = 0;
        if (TryFindMemberOffset(typeId, "_Myfirst", out offFirst) &&
            TryFindMemberOffset(typeId, "_Mylast", out offLast))
            return true;
        if (TryFindMemberOffset(typeId, "_Myval2", out var offMyval2))
        {
            offFirst = offMyval2;
            offLast = offMyval2 + 4;
            return true;
        }

        if (objSize == 12)
        {
            offFirst = 0;
            offLast = 4;
            return true;
        }

        return false;
    }

    private bool ResolveMapPointerOffsets(uint typeId, uint objSize, bool fastMap, out uint offHead, out uint offSize)
    {
        offHead = 0;
        offSize = 0;
        if (fastMap && objSize == 8)
        {
            offHead = 0;
            offSize = 4;
            return true;
        }

        if (TryFindMemberOffset(typeId, "_Myhead", out offHead) &&
            TryFindMemberOffset(typeId, "_Mysize", out offSize))
            return true;

        if (objSize == 8)
        {
            offHead = 0;
            offSize = 4;
            return true;
        }

        return false;
    }

    private static bool VectorCountFromPointers(uint first, uint last, uint elemSize, out uint count)
    {
        count = 0;
        if (elemSize == 0)
            return false;
        if (first == 0 && last == 0)
            return true;
        if (last < first || (last - first) % elemSize != 0)
            return false;
        count = (last - first) / elemSize;
        return count <= 256;
    }

    private static bool MapPtrIsSentinel(uint myhead, uint ptr, KitMemoryAccess memory)
    {
        if (ptr == 0 || ptr == myhead)
            return true;
        var isnil = memory.ReadByte((nuint)(ptr + MapNodeOffIsNil));
        return isnil is null or not 0;
    }

    private static bool MapTreeMinimum(uint myhead, KitMemoryAccess memory, out uint node)
    {
        node = 0;
        var left = memory.ReadDword((nuint)(myhead + MapNodeOffLeft));
        if (left is null)
            return false;
        if (MapPtrIsSentinel(myhead, left.Value, memory))
            return true;
        node = left.Value;
        return true;
    }

    private static bool MapTreeMinChild(uint myhead, uint subtree, KitMemoryAccess memory, out uint node)
    {
        node = 0;
        if (MapPtrIsSentinel(myhead, subtree, memory))
            return false;
        node = subtree;
        for (var step = 0; step < MapTreeMaxSteps; step++)
        {
            var left = memory.ReadDword((nuint)(subtree + MapNodeOffLeft));
            if (left is null)
                return false;
            if (MapPtrIsSentinel(myhead, left.Value, memory))
                return true;
            subtree = left.Value;
            node = subtree;
        }

        return false;
    }

    private static bool MapTreeSuccessor(uint myhead, uint node, KitMemoryAccess memory, out uint next)
    {
        next = 0;
        if (node == 0 || MapPtrIsSentinel(myhead, node, memory))
            return false;

        var right = memory.ReadDword((nuint)(node + MapNodeOffRight));
        if (right is null)
            return false;
        if (!MapPtrIsSentinel(myhead, right.Value, memory))
            return MapTreeMinChild(myhead, right.Value, memory, out next);

        for (var step = 0; step < MapTreeMaxSteps; step++)
        {
            var parent = memory.ReadDword((nuint)(node + MapNodeOffParent));
            if (parent is null)
                return false;
            if (MapPtrIsSentinel(myhead, parent.Value, memory))
                return true;

            var parentRight = memory.ReadDword((nuint)(parent.Value + MapNodeOffRight));
            if (parentRight is null)
                return false;
            if (node != parentRight.Value)
            {
                next = parent.Value;
                return true;
            }

            node = parent.Value;
        }

        return false;
    }
}
