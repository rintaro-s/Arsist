# Unity ビルドテスト手順

## コンパイルエラーの修正完了

以下の修正を実施しました：

### 1. `AndroidJavaObject.GetRawClass()` エラーの修正
**ファイル**: `ArsistWebViewUI.cs`

**修正前** (エラー):
```csharp
AndroidJavaObject.GetRawClass("android.view.View$MeasureSpec").CallStatic<int>("makeMeasureSpec", width, 1073741824)
```

**修正後**:
```csharp
var measureSpecClass = new AndroidJavaClass("android.view.View$MeasureSpec");
int widthMeasureSpec = measureSpecClass.CallStatic<int>("makeMeasureSpec", width, 1073741824);
webView.Call("measure", widthMeasureSpec, heightMeasureSpec);
```

### 2. `WebViewClientProxy` の修正
不要な `GetAndroidJavaObject()` メソッドを削除し、AndroidJavaProxyを直接渡すように修正。

---

## コンパイル確認方法

### オプション1: Unityエディタで確認

1. Unityエディタを開く
   ```
   Unity Hub → ArsistBuilder プロジェクトを開く
   ```

2. スクリプトの再コンパイルを待つ
   - Console ウィンドウでエラーがないことを確認
   - エラーがある場合は表示される

3. 手動で再コンパイルを強制
   - メニュー: `Assets` → `Reimport All`
   - または: `Ctrl+R` (Windows)

### オプション2: コマンドラインでビルドテスト

```powershell
# Arsistプロジェクトのルートディレクトリで実行
cd E:\github\Arsist

# Unityのbatchmodeでコンパイルのみテスト
# (Unity.exeのパスは環境に応じて変更)
& "C:\Program Files\Unity\Hub\Editor\2022.3.x\Editor\Unity.exe" `
  -batchmode `
  -nographics `
  -projectPath "E:\github\Arsist\UnityBackend\ArsistBuilder" `
  -quit `
  -logFile "E:\github\Arsist\unity_compile_test.log"

# ログファイルでエラーを確認
Get-Content "E:\github\Arsist\unity_compile_test.log" | Select-String -Pattern "error|Error|ERROR" -Context 2
```

### オプション3: npm経由でビルド実行

```powershell
# Arsistエンジンからビルド実行（フルビルド）
npm run dev

# または
node scripts/run-unity-build.js
```

---

## エラーが残っている場合

### よくある問題と対処法

#### 1. "Assets/Arsist/Runtime/UI/ArsistWebViewUI.cs(...): error CS..."
→ ファイルが正しく保存されていない可能性
- Unityエディタを再起動
- `Assets` → `Refresh` (Ctrl+R)

#### 2. "namespace 'AndroidJavaClass' could not be found"
→ Android Build Supportがインストールされていない
- Unity Hub → Installs → プラットフォームを追加 → Android

#### 3. ライセンスエラー
→ 無視して問題ありません（指示通り）

---

## 修正済みファイル一覧

- ✅ `UnityBackend/ArsistBuilder/Assets/Arsist/Runtime/UI/ArsistWebViewUI.cs`
  - line 450-454: AndroidJavaClass使用に修正
  - line 461: AndroidJavaProxyを直接渡すように修正
  - line 488-504: 不要なメソッド削除

- ✅ `UnityBackend/ArsistBuilder/Assets/Arsist/Editor/ArsistBuildPipeline.cs`
  - line 177-225: GLB回転の親配置後適用
  - line 240-272: localEulerAngles使用

---

## 次のステップ

1. **コンパイル確認**: 上記いずれかの方法でコンパイルエラーがないことを確認
2. **ビルド実行**: APKをビルド
3. **実機テスト**: XREAL One + Beam Proで動作確認

コンパイルエラーが解消されない場合は、エラーメッセージの全文を共有してください。
