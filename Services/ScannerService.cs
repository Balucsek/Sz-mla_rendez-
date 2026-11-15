using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace InvoiceManager.Services;

public sealed class ScannerDevice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public sealed class ScannerService
{
    private const string JpegFormatId = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

    public List<ScannerDevice> GetAvailableScanners()
    {
        var managerType = Type.GetTypeFromProgID("WIA.DeviceManager");
        if (managerType is null)
        {
            return new List<ScannerDevice>();
        }

        object? manager = null;
        object? deviceInfos = null;
        var scanners = new List<ScannerDevice>();
        try
        {
            manager = Activator.CreateInstance(managerType)
                ?? throw new InvalidOperationException("Nem sikerült elindítani a Windows szkennerkezelőjét.");
            dynamic wiaManager = manager;
            deviceInfos = wiaManager.DeviceInfos;
            dynamic wiaDeviceInfos = deviceInfos;
            var count = (int)wiaDeviceInfos.Count;

            for (var index = 1; index <= count; index++)
            {
                object? deviceInfo = null;
                object? properties = null;
                object? nameProperty = null;
                try
                {
                    deviceInfo = wiaDeviceInfos.Item(index);
                    dynamic wiaDeviceInfo = deviceInfo;
                    if ((int)wiaDeviceInfo.Type != 1)
                    {
                        continue;
                    }

                    var id = (string)wiaDeviceInfo.DeviceID;
                    properties = wiaDeviceInfo.Properties;
                    dynamic wiaProperties = properties;
                    nameProperty = wiaProperties.Item(7);
                    dynamic wiaNameProperty = nameProperty;
                    var name = Convert.ToString(wiaNameProperty.Value);
                    scanners.Add(new ScannerDevice
                    {
                        Id = id,
                        Name = string.IsNullOrWhiteSpace(name) ? id : name
                    });
                }
                finally
                {
                    ReleaseComObject(nameProperty);
                    ReleaseComObject(properties);
                    ReleaseComObject(deviceInfo);
                }
            }

            return scanners;
        }
        finally
        {
            ReleaseComObject(deviceInfos);
            ReleaseComObject(manager);
        }
    }

    public string? ScanToPdf(string scanFolder, string scannerDeviceId)
    {
        if (string.IsNullOrWhiteSpace(scannerDeviceId))
        {
            throw new InvalidOperationException("Nincs kiválasztott szkenner. Válasszon eszközt a Beállításokban.");
        }

        Directory.CreateDirectory(scanFolder);
        var temporaryImagePath = Path.Combine(Path.GetTempPath(), $"szamlarendezo-{Guid.NewGuid():N}.jpg");
        var pdfPath = GetUniquePdfPath(scanFolder, $"Szamla_scan_{DateTime.Now:yyyy-MM-dd_HHmmss}.pdf");
        object? manager = null;
        object? deviceInfos = null;
        object? deviceInfo = null;
        object? device = null;
        object? items = null;
        object? item = null;
        object? scannedImage = null;
        var pdfCreated = false;

        try
        {
            var managerType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (managerType is null)
            {
                throw new InvalidOperationException("A Windows Image Acquisition (WIA) szolgáltatás nem érhető el ezen a számítógépen.");
            }

            manager = Activator.CreateInstance(managerType)
                ?? throw new InvalidOperationException("Nem sikerült elindítani a Windows szkennerkezelőjét.");
            dynamic wiaManager = manager;
            deviceInfos = wiaManager.DeviceInfos;
            dynamic wiaDeviceInfos = deviceInfos;
            var count = (int)wiaDeviceInfos.Count;
            for (var index = 1; index <= count; index++)
            {
                object? candidate = null;
                try
                {
                    candidate = wiaDeviceInfos.Item(index);
                    dynamic wiaCandidate = candidate;
                    if (string.Equals((string)wiaCandidate.DeviceID, scannerDeviceId, StringComparison.Ordinal))
                    {
                        deviceInfo = candidate;
                        candidate = null;
                        break;
                    }
                }
                finally
                {
                    ReleaseComObject(candidate);
                }
            }

            if (deviceInfo is null)
            {
                throw new InvalidOperationException(
                    "A kiválasztott szkenner már nem érhető el. Frissítse a szkennerek listáját a Beállításokban.");
            }

            dynamic wiaDeviceInfo = deviceInfo;
            device = wiaDeviceInfo.Connect();
            dynamic wiaDevice = device;
            items = wiaDevice.Items;
            dynamic wiaItems = items;
            item = wiaItems.Item(1);
            dynamic wiaItem = item;
            scannedImage = wiaItem.Transfer(JpegFormatId);
            if (scannedImage is null)
            {
                throw new InvalidOperationException("A szkenner nem adott vissza beolvasható képet.");
            }

            dynamic wiaImage = scannedImage;
            wiaImage.SaveFile(temporaryImagePath);

            using (var image = XImage.FromFile(temporaryImagePath))
            using (var document = new PdfDocument())
            {
                var page = document.AddPage();
                page.Width = image.PointWidth;
                page.Height = image.PointHeight;
                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
                document.Save(pdfPath);
            }

            pdfCreated = true;
            return pdfPath;
        }
        finally
        {
            ReleaseComObject(scannedImage);
            ReleaseComObject(item);
            ReleaseComObject(items);
            ReleaseComObject(device);
            ReleaseComObject(deviceInfo);
            ReleaseComObject(deviceInfos);
            ReleaseComObject(manager);
            if (File.Exists(temporaryImagePath))
            {
                File.Delete(temporaryImagePath);
            }

            if (!pdfCreated && File.Exists(pdfPath))
            {
                File.Delete(pdfPath);
            }
        }
    }

    private static string GetUniquePdfPath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{name} ({suffix}).pdf");
            suffix++;
        }

        return candidate;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
