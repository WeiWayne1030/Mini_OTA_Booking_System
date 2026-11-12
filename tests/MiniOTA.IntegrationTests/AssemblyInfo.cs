using Xunit;

// 所有整合測試共用同一個 miniota_test 資料庫，
// 若讓不同測試類別平行執行，一個測試清資料會破壞另一個測試。
[assembly: CollectionBehavior(DisableTestParallelization = true)]