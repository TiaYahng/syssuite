// SysSuite.Native —— SMART 共享设施：设备打开、权限判定、型号字段填充
#include "native_smart_internal.h"

#include <winioctl.h>
#include <ntddstor.h>

#include <cstring>
#include <cwchar>

namespace syssuite::smart {

namespace {

// 把设备返回的 ASCII 串按字节扩展为宽字符并去除首尾空白
void CopyWide(wchar_t* destination, size_t capacity, const char* source)
{
    if (capacity == 0)
    {
        return;
    }

    if (source == nullptr)
    {
        destination[0] = L'\0';
        return;
    }

    size_t start = 0;
    size_t length = std::strlen(source);
    while (start < length && (source[start] == ' ' || source[start] == '\0'))
    {
        ++start;
    }
    while (length > start && (source[length - 1] == ' ' || source[length - 1] == '\0'))
    {
        --length;
    }

    size_t index = 0;
    for (size_t i = start; i < length && index + 1 < capacity; ++i)
    {
        destination[index++] = static_cast<wchar_t>(static_cast<unsigned char>(source[i]));
    }
    destination[index] = L'\0';
}

}  // namespace

OpenResult OpenPhysicalDrive(uint32_t driveNumber, DWORD access, HANDLE& device)
{
    wchar_t path[64]{};
    if (swprintf_s(path, L"\\\\.\\PhysicalDrive%u", driveNumber) <= 0)
    {
        device = INVALID_HANDLE_VALUE;
        return OpenResult::NotFound;
    }

    device = CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE,
                         nullptr, OPEN_EXISTING, 0, nullptr);
    if (device != INVALID_HANDLE_VALUE)
    {
        return OpenResult::Success;
    }

    const DWORD error = GetLastError();
    if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
    {
        return OpenResult::NotFound;
    }

    if (error == ERROR_ACCESS_DENIED || error == ERROR_PRIVILEGE_NOT_HELD)
    {
        return OpenResult::AccessDenied;
    }

    return OpenResult::NotFound;
}

void FillModelFields(NativeSmartInfo& info, HANDLE device)
{
    STORAGE_PROPERTY_QUERY query{};
    query.PropertyId = StorageDeviceProperty;
    query.QueryType = PropertyStandardQuery;

    BYTE buffer[1024]{};
    DWORD returned = 0;
    if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, &query, sizeof(query),
                         buffer, sizeof(buffer), &returned, nullptr))
    {
        return;
    }

    auto* descriptor = reinterpret_cast<STORAGE_DEVICE_DESCRIPTOR*>(buffer);
    auto* base = reinterpret_cast<const char*>(buffer);
    const DWORD size = returned;

    if (descriptor->ProductIdOffset != 0 && descriptor->ProductIdOffset < size)
    {
        CopyWide(info.model, 64, base + descriptor->ProductIdOffset);
    }
    if (descriptor->SerialNumberOffset != 0 && descriptor->SerialNumberOffset < size)
    {
        CopyWide(info.serial, 64, base + descriptor->SerialNumberOffset);
    }
    if (descriptor->ProductRevisionOffset != 0 && descriptor->ProductRevisionOffset < size)
    {
        CopyWide(info.firmware, 16, base + descriptor->ProductRevisionOffset);
    }
}

}  // namespace syssuite::smart
