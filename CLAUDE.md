# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

Slay the Spire 2 MOD 向けの汎用 HTTP サーバーライブラリ。Godot・STS2 ゲーム DLL に依存しない純粋な .NET 実装。複数の MOD から `ProjectReference` または git submodule として参照される。

## ビルド・開発コマンド

```bash
# ライブラリのビルド（tests/ は含まれない）
dotnet build Sts2ServerLib.csproj

# テスト実行
dotnet test tests/Sts2ServerLib.Tests.csproj

# 単一テストの実行（--filter でテスト名を指定）
dotnet test tests/Sts2ServerLib.Tests.csproj --filter "パストラバーサル_403を返すこと"
```

## アーキテクチャ

単一クラス `StateServer` (`StateServer.cs`) のみで構成されるシンプルなライブラリ。

### StateServer クラスの設計

- `System.Net.HttpListener` を使用して `localhost:{port}` で待ち受け（デフォルト: 21345）
- `GET /state` → `getStateJson()` 関数を呼び出してゲーム状態 JSON を返す
- それ以外のパス → `webRoot` ディレクトリから静的ファイルを配信（`webRoot` が null なら 404）
- `ListenLoop()` はバックグラウンドタスクとして動作し、`CancellationToken` で停止制御

### 依存注入パターン

コンストラクタで以下を注入する設計:
- `getStateJson`: ゲーム状態を JSON 文字列で返す関数（MOD 側が実装）
- `log` / `logErr`: ログ出力関数（未指定時は `Console.WriteLine` / `Console.Error.WriteLine`）
- `webRoot`: 静的ファイル配信ルートディレクトリ（省略可）

この設計により、Godot なしの環境（テスト等）でもライブラリ単体で動作する。

### 親プロジェクトからの参照方法

`Directory.Build.props` の `Sts2ServerLibRoot` プロパティで制御:
- `../Sts2ServerLib/Sts2ServerLib.csproj` が存在する場合 → sibling ローカルリポジトリを優先
- 存在しない場合 → `server/` submodule を使用

### セキュリティ

- **パストラバーサル対策**: `Path.GetFullPath` で正規化後、`webRoot` 外のパスは 403 を返す
- **CORS**: `Access-Control-Allow-Origin: *` を全レスポンスに付与
- **OPTIONS プリフライト**: 204 で即応答
