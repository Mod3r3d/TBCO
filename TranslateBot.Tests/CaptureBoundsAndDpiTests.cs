using System;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TranslateBot.Capture;

namespace TranslateBot.Tests
{
    [TestClass]
    public class CaptureBoundsAndDpiTests
    {
        [TestMethod]
        public void CaptureService_InvalidZeroDimensions_ReturnsEmptyStatus()
        {
            using var service = new CaptureService();
            var res = service.CaptureRegionWithResult(100, 100, 0, 0);

            Assert.IsFalse(res.IsSuccess);
            Assert.AreEqual(CaptureStatus.Empty, res.Status);
        }

        [TestMethod]
        public void CaptureService_InvalidNegativeDimensions_ReturnsEmptyStatus()
        {
            using var service = new CaptureService();
            var res = service.CaptureRegionWithResult(100, 100, -50, 100);

            Assert.IsFalse(res.IsSuccess);
            Assert.AreEqual(CaptureStatus.Empty, res.Status);
        }

        [TestMethod]
        public void CaptureService_InvalidWindowHandle_ReturnsInvalidWindowStatus()
        {
            using var service = new CaptureService();
            var res = service.CaptureWindowRegionWithResult(IntPtr.Zero, 0, 0, 800, 600);

            Assert.IsFalse(res.IsSuccess);
            Assert.AreEqual(CaptureStatus.InvalidWindow, res.Status);
        }

        [TestMethod]
        public void CaptureService_NonExistentWindowHandle_ReturnsInvalidWindowStatus()
        {
            using var service = new CaptureService();
            // Handle ngẫu nhiên không tồn tại
            var fakeHandle = new IntPtr(0x12345678);
            var res = service.CaptureWindowRegionWithResult(fakeHandle, 0, 0, 800, 600);

            Assert.IsFalse(res.IsSuccess);
            Assert.AreEqual(CaptureStatus.InvalidWindow, res.Status);
        }

        [TestMethod]
        public void CaptureService_OutOfBoundsCoordinates_ReturnsOutOfBoundsStatus()
        {
            using var service = new CaptureService();
            // Toạ độ phi lý nằm ngoài toàn bộ hệ thống màn hình (ví dụ X=999999, Y=999999)
            var res = service.CaptureRegionWithResult(999999, 999999, 800, 600);

            Assert.IsFalse(res.IsSuccess);
            Assert.AreEqual(CaptureStatus.OutOfBounds, res.Status);
        }

        [TestMethod]
        public void CaptureBufferPool_ReusesBuffer_WhenSameDimensionsRequested()
        {
            using var pool = new CaptureBufferPool();
            var buf1 = pool.Capture("slot_test", 0, 0, 100, 100);
            var buf2 = pool.Capture("slot_test", 0, 0, 100, 100);

            // Cùng kích thước và cùng slot -> Phải tái sử dụng cùng tham chiếu byte array (zero allocation)
            Assert.AreSame(buf1, buf2, "BufferPool phải tái sử dụng mảng byte cùng kích thước trong cùng slot");
        }

        [TestMethod]
        public void CaptureService_Cloned_ProducesIndependentBuffer()
        {
            using var service = new CaptureService();
            var vBounds = NativeMethods.GetVirtualScreenBounds();
            // Chụp thử vùng nhỏ 10x10 trong màn hình ảo
            var clone = service.CaptureRegionCloned(vBounds.X + 10, vBounds.Y + 10, 10, 10);

            if (clone.Length > 0)
            {
                // Thay đổi byte trong clone không ảnh hưởng tới pool
                byte orig = clone[0];
                clone[0] = (byte)(orig ^ 0xFF);
                var fresh = service.CaptureRegion(vBounds.X + 10, vBounds.Y + 10, 10, 10, "cloned_temp");

                Assert.AreNotEqual(clone[0], fresh[0], "Bản cloned phải là mảng độc lập, không dùng chung bộ nhớ với slot");
            }
        }

        [TestMethod]
        public void NativeMethods_VirtualScreenBounds_ReturnsValidArea()
        {
            var bounds = NativeMethods.GetVirtualScreenBounds();
            Assert.IsTrue(bounds.Width > 0, "VirtualScreen Width phải > 0");
            Assert.IsTrue(bounds.Height > 0, "VirtualScreen Height phải > 0");
        }
    }
}
