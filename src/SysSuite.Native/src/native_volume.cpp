// SysSuite.Native —— MFT / USN 卷扫描（T3.2）
//
// 设计说明：
//   * 走 FSCTL_ENUM_USN_DATA 顺序读 USN 日志，比 FindFirstFile 递归少掉层层 opendir，
//     这是它能快一个数量级的全部原因 —— 逻辑本身没有任何魔法。
//   * 全程只读：不写 USN 日志、不删除日志、不修改任何文件。
//   * 分批回调，每批至多 NATIVE_SCAN_BATCH_MAX 条，避免调用方单次分配过大。
//   * 条目的 name **直接指向输出缓冲**：一次零拷贝。代价是回调返回后指针即失效，
//     调用方必须自己留档 —— 这个取舍写进了 native_api.h 的注释里。
#include "native_api.h"

#include <windows.h>
#include <winioctl.h>

#include <cstddef>
#include <cstring>
#include <vector>

namespace {

constexpr DWORD kOutputBytes = 1024 * 1024;   // 单次 DeviceIoControl 输出缓冲
constexpr uint32_t kBatchMax = NATIVE_SCAN_BATCH_MAX;

// USN_RECORD_V2 / V3 / V4 的前置字段布局一致，按偏移取值即可同时兼容三种版本，
// 不必为每种版本各写一份解析。
constexpr std::size_t kOffsetRecordLength = 0;
constexpr std::size_t kOffsetFileReference = 8;
constexpr std::size_t kOffsetParentReference = 16;
constexpr std::size_t kOffsetAttributes = 52;
constexpr std::size_t kOffsetNameLength = 56;
constexpr std::size_t kOffsetNameOffset = 58;
constexpr std::size_t kRecordHeaderBytes = 60;

int TranslateOpenError(DWORD error)
{
    return error == ERROR_ACCESS_DENIED ? NATIVE_ERR_ACCESS_DENIED : NATIVE_ERR_IO;
}

}  // namespace

class VolumeScanner
{
public:
    ~VolumeScanner()
    {
        if (volume_ != INVALID_HANDLE_VALUE)
        {
            ::CloseHandle(volume_);
        }
    }

    int Open(const wchar_t* path)
    {
        volume_ = ::CreateFileW(path,
                                GENERIC_READ,
                                FILE_SHARE_READ | FILE_SHARE_WRITE,
                                nullptr,
                                OPEN_EXISTING,
                                FILE_ATTRIBUTE_NORMAL,
                                nullptr);
        return volume_ == INVALID_HANDLE_VALUE ? TranslateOpenError(::GetLastError()) : NATIVE_OK;
    }

    int Enumerate(NativeFileEntryCallback onBatch, void* context, NativeCancelCheck isCancelled)
    {
        buffer_.resize(kOutputBytes);
        MFT_ENUM_DATA_V0 input{};
        input.LowUsn = 0;
        input.HighUsn = MAXLONGLONG;

        uint64_t processed = 0;
        for (;;)
        {
            if (isCancelled != nullptr && isCancelled(context) != 0)
            {
                return NATIVE_ERR_CANCELLED;
            }

            DWORD returned = 0;
            if (!::DeviceIoControl(volume_,
                                   FSCTL_ENUM_USN_DATA,
                                   &input,
                                   sizeof(input),
                                   buffer_.data(),
                                   static_cast<DWORD>(buffer_.size()),
                                   &returned,
                                   nullptr))
            {
                const DWORD error = ::GetLastError();
                // ERROR_HANDLE_EOF 是正常枚举结束信号，不是失败
                if (error == ERROR_HANDLE_EOF)
                {
                    break;
                }

                return error == ERROR_ACCESS_DENIED ? NATIVE_ERR_ACCESS_DENIED : NATIVE_ERR_IO;
            }

            if (returned < sizeof(uint64_t))
            {
                break;
            }

            std::memcpy(&input.StartFileReferenceNumber, buffer_.data(), sizeof(uint64_t));

            std::size_t offset = sizeof(uint64_t);
            while (offset + kRecordHeaderBytes <= returned)
            {
                const uint32_t recordLength = ReadU32(buffer_.data(), offset + kOffsetRecordLength);
                if (recordLength < kRecordHeaderBytes || offset + recordLength > returned)
                {
                    break;
                }

                // 用 32 位承接 16 位字段：与 recordLength 比较时避免整型提升带来的符号混比
                const uint32_t nameLength = ReadU16(buffer_.data(), offset + kOffsetNameLength);
                const uint32_t nameOffset = ReadU16(buffer_.data(), offset + kOffsetNameOffset);
                if (nameOffset + nameLength <= recordLength)
                {
                    NativeFileEntry entry{};
                    entry.fileReference = ReadU64(buffer_.data(), offset + kOffsetFileReference);
                    entry.parentReference = ReadU64(buffer_.data(), offset + kOffsetParentReference);
                    entry.attributes = ReadU32(buffer_.data(), offset + kOffsetAttributes);
                    entry.nameLength = nameLength;
                    entry.name = reinterpret_cast<const wchar_t*>(buffer_.data() + offset + nameOffset);

                    // 名字指向缓冲区，本批回调返回前必须完成，故满批立即送出
                    if (batch_.size() >= kBatchMax && onBatch != nullptr)
                    {
                        const int verdict = Deliver(onBatch, context, processed);
                        if (verdict != 0)
                        {
                            return NATIVE_ERR_CANCELLED;
                        }
                    }

                    batch_.push_back(entry);
                }

                offset += recordLength;
            }

            if (onBatch != nullptr && !batch_.empty())
            {
                const int verdict = Deliver(onBatch, context, processed);
                if (verdict != 0)
                {
                    return NATIVE_ERR_CANCELLED;
                }
            }
        }

        return NATIVE_OK;
    }

private:
    int Deliver(NativeFileEntryCallback onBatch, void* context, uint64_t& processed)
    {
        const uint32_t count = static_cast<uint32_t>(batch_.size());
        if (count == 0)
        {
            return 0;
        }

        processed += count;
        const int verdict = onBatch(context, batch_.data(), count, processed, processed);
        batch_.clear();
        return verdict;
    }

    static uint16_t ReadU16(const BYTE* data, std::size_t offset)
    {
        uint16_t value = 0;
        std::memcpy(&value, data + offset, sizeof(value));
        return value;
    }

    static uint32_t ReadU32(const BYTE* data, std::size_t offset)
    {
        uint32_t value = 0;
        std::memcpy(&value, data + offset, sizeof(value));
        return value;
    }

    static uint64_t ReadU64(const BYTE* data, std::size_t offset)
    {
        uint64_t value = 0;
        std::memcpy(&value, data + offset, sizeof(value));
        return value;
    }

    HANDLE volume_ = INVALID_HANDLE_VALUE;
    std::vector<BYTE> buffer_;
    std::vector<NativeFileEntry> batch_;
};

extern "C" int NATIVE_CALL Native_ScanVolume2(const wchar_t* volume,
                                              NativeFileEntryCallback onBatch,
                                              void* context,
                                              NativeCancelCheck isCancelled)
{
    if (volume == nullptr || volume[0] == L'\0')
    {
        return NATIVE_ERR_INVALID_ARG;
    }

    // 契约：进入扫描前先做一次取消检查，保证"请求即取消"可被确定性观测
    if (isCancelled != nullptr && isCancelled(context) != 0)
    {
        return NATIVE_ERR_CANCELLED;
    }

    VolumeScanner scanner;
    const int opened = scanner.Open(volume);
    if (opened != NATIVE_OK)
    {
        return opened;
    }

    return scanner.Enumerate(onBatch, context, isCancelled);
}
