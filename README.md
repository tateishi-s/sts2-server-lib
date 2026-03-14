# Sts2ServerLib

Slay the Spire 2 MOD 向けの汎用 HTTP サーバーライブラリ。

Godot・STS2 ゲーム DLL に依存しない純粋な .NET 実装のため、複数の MOD から再利用可能。

## 機能

- `GET /state` エンドポイントでゲーム状態を JSON 形式で配信
- 静的ファイル配信（HTML / CSS / JS / PNG / ICO）
- CORS 対応（Web UI からの fetch を許可）
- パストラバーサル対策

## 使い方

### 基本的な使用例

```csharp
using Sts2ServerLib;

var server = new StateServer(
    getStateJson: () => GetCurrentStateAsJson(),  // ゲーム状態を返す関数
    port: 21345,
    webRoot: "/path/to/web/ui",                   // 静的ファイルのルートディレクトリ（省略可）
    log: msg => GD.Print(msg),                    // ログ出力（省略時: Console.WriteLine）
    logErr: msg => GD.PrintErr(msg)               // エラーログ（省略時: Console.Error.WriteLine）
);

server.Start();
// → http://localhost:21345/ でリッスン開始

// 終了時
server.Dispose();
```

### エンドポイント

| パス | メソッド | 説明 |
|------|----------|------|
| `/state` | GET | `getStateJson()` の戻り値を JSON で返す |
| `/*` | GET | `webRoot` 以下の静的ファイルを返す |
| `/*` | OPTIONS | CORS プリフライトに 204 で応答 |

## プロジェクトへの組み込み

### git submodule として追加

```bash
git submodule add https://github.com/tateishi-s/Sts2ServerLib server
```

### ProjectReference で参照

```xml
<!-- mod/YourMod.csproj -->
<ItemGroup>
  <ProjectReference Include="../Sts2ServerLib/Sts2ServerLib.csproj" />
</ItemGroup>
```

`Directory.Build.props` で sibling ローカルリポジトリと submodule を自動切り替えする方法は [docs/Sts2ServerLib-design.md](docs/Sts2ServerLib-design.md) を参照。

## ビルド

```bash
dotnet build Sts2ServerLib.csproj
```

外部依存なし（標準 .NET API のみ使用）。

## 動作環境

- .NET 9.0 以上
