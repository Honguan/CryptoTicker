# CryptoTicker

Windows 加密貨幣即時行情與技術分析懸浮工具。

## 功能

- 整合 Binance、OKX、Bybit 即時現貨報價，以中位數顯示主價格。
- 支援自訂 REST JSON 行情來源與欄位路徑。
- 提供 15 分鐘、1 小時、4 小時的 EMA、RSI、MACD 趨勢分析。
- 可選用 OpenAI 相容 API 產生文字解讀；API 金鑰保存在 Windows 認證管理員。
- 包含懸浮球、系統匣、免安裝版與 Inno Setup 安裝程式。

## 安裝與使用

從 [Releases](https://github.com/Honguan/CryptoTicker/releases) 下載 `CryptoTicker-Setup.exe` 並執行。安裝後，懸浮球會顯示選定交易對的整合價格；雙擊可開啟完整分析與設定。

分析結果僅供資訊參考，不構成投資建議。

## 建置

```powershell
dotnet run --project CryptoTicker.CoreSelfTest\CryptoTicker.CoreSelfTest.csproj
dotnet build CryptoTicker.Desktop\CryptoTicker.Desktop.csproj
dotnet publish CryptoTicker.Desktop\CryptoTicker.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64 --source https://api.nuget.org/v3/index.json
& 'C:\Program Files\Inno Setup 7\ISCC.exe' 'installer\CryptoTicker.iss'
```

安裝檔會產生於 `publish\installer\CryptoTicker-Setup.exe`。
