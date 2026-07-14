# unitypackage-to-vpm

`.unitypackage` を VPM (VRChat Package Manager) 形式のパッケージディレクトリに変換するCLIツールです。

## できること

- `.unitypackage` を展開し、`Assets/` プレフィックスを取り除いて `Packages/<name>/Runtime/` 配下に再配置
- 複数の `.unitypackage` を指定した場合、順番にマージ（共通マテリアルを使用し、その後アバター依存のパッケージを追加するようなアセットを想定しています）
- プレースホルダの `package.json` を生成（`legacyFolders` も付与）
- `.meta` が欠けているファイル/フォルダがあれば、ローカルのUnity Editorをバッチモードで起動して自動生成
- `Assets/` のハードコードパスや `Application.dataPath` 参照、プリコンパイル済み `.dll` の混入を簡易的に検出して警告

## 使い方

```
unitypackage-to-vpm [--unity-path <path>] <output-vpm-directory> <input1.unitypackage> [input2.unitypackage ...]
```

例

```
unitypackage-to-vpm ./MyPackage ./Downloads/SomeAsset.unitypackage
```

複数パッケージをまとめて1つのVPMパッケージにする場合

```
unitypackage-to-vpm ./MyPackage ./base.unitypackage ./extra.unitypackage
```

### Unity Editorのパス指定

出力先に `.meta` が欠けているアセットが1つでもあると、Unityをバッチモードで起動して補完します。Unityの実行ファイルは以下の優先順位で探索されます。

1. `--unity-path <path>` / `-u <path>` / `--unity-path=<path>`
2. `UNITY_PATH` 環境変数
3. Unity Hubの標準インストール先を自動探索（Windows/macOSのみ、複数バージョンがあれば最新のものを使用）

Linuxでの自動探索には対応していないので、Linux環境では `--unity-path` か `UNITY_PATH` を明示してください。

対応するUnityが見つからない、またはすべての `.meta` が既に揃っている場合はUnityの起動自体をスキップします。

## ビルド

```
dotnet publish src/UnityPackageToVpm/UnityPackageToVpm.csproj -c Release -r <RID> -o publish/<RID>
```

`-c Release` でビルドするとNativeAOTでコンパイルされます（`osx-arm64` / `osx-x64` / `win-x64` で動作確認済み。Linux向けのビルド/リリースは現時点で未提供）。

## 注意点

- **package.jsonはプレースホルダのまま出力されます。** `version`（`0.1.0`固定）、`author`、`description`、`vpmDependencies` などは空のまま生成されるため、リリース前に必ず自分で編集してください。
    - name: ハイフン区切りの小文字英数字で、VPMのパッケージ名として有効な形式にしてください。
    - version: リリース時には必ずSemVer形式のバージョンに書き換えてください。
    - displayName: VPMのUIに表示される名前です。日本語を含むとUnity Editorから参照できない場合があるので、英数字のみの名前を推奨します。
- **GUID衝突時は破壊的に上書きします。** 複数の`.unitypackage`を渡した場合、同じGUIDのアセットが別パスに存在すると古いファイルは削除されます。意図した挙動か確認してから使ってください。
- **互換性チェックは簡易的な静的検査です。** `Assets/`のハードコードやDLL内の`Application.dataPath`利用などを検出しますが、これは「壊れる可能性がある」ことを知らせるだけで、実際に動くかどうかの保証にはなりません。
- **.metaの自動生成にはUnity Editorが必要になる場合があります。** 入力パッケージの中に`.meta`が欠けたアセットが含まれていると、ローカルにインストールされたUnityを起動して補完します。CI環境などUnityが無い場所で使う場合は、あらかじめ`.meta`が揃った状態のパッケージを使うか、事前にUnityをインストールしてパスを指定してください。
- **Unityの補完に失敗しても、コマンド自体は正常終了扱いになります。** Unity起動が失敗した場合はエラーログを出力先に`unity_meta_gen.log`として保存しますが、コマンドの終了コードはそれとは連動していません。実行後のログ出力（`All assets already had .meta files` / `invoking Unity to generate them` などのメッセージやエラー出力）を確認してください。
- 一時プロジェクトの `Assets` フォルダをシンボリックリンクとして作成するため、シンボリックリンクの作成が制限された環境（一部のWindows設定など）ではUnityによる`.meta`補完が失敗する可能性があります。
