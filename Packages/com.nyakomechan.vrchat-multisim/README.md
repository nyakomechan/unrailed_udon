# VRChat MultiSim
ClientSim に Local TCP通信機能を付加し、 Unity Editor 上で VRChat ワールドのマルチプレイヤーでの動作確認を行うツールです。

[ParrelSync](https://github.com/VeriorPies/ParrelSync/) を用いて複製したプレイヤー人数分の Unity Editor にて、通常のClientSimと同じ感覚でマルチプレイヤーでの動作確認を行えます。

同期の遅延（ VRC Object Sync や変数同期で生じる遅延）については再現していません。

VRChat ワールドの同期ギミック(同期変数、ネットワークイベント、Ownership、ピックアップ、Station など)を作っていて、こんな経験はありませんか?

- 動作確認のたびにビルド → アップロード → VRChat 起動 → 2アカウント or 2台で入り直し…と数分〜数十分かかる
- ClientSim(Play ボタンでのテスト)は 1 人プレイなので、「他のプレイヤーから見てどう動くか」「レイトジョイナーに状態が引き継がれるか」が確認できない
- 「自分の環境では動いたのに、フレンドと入ったら同期がズレていた」

MultiSim を入れると、[ParrelSync](https://github.com/VeriorPies/ParrelSync/) で複製した 2 つ(以上)の Unity Editor がお互いに通信し、**それぞれが別のプレイヤーとして同じワールドに入った状態**になります。片方の Editor でボタンを押せば、もう片方の Editor でも同期変数が更新され、`OnDeserialization` やネットワークイベントが発火します。コードを直したら Play し直すだけ。ビルドもアップロードも不要です。

## 仕組み(ざっくり)

- ParrelSync の**オリジナルプロジェクトがホスト**(プレイヤーID 1、マスター)、**クローンがクライアント**として自動で判別されます。設定の使い分けは不要です。
- 中身は VRChat SDK 同梱の ClientSim をそのまま使い、そこに Editor 間の通信レイヤー(localhost TCP)を追加しています。Udon の開始タイミングを全 Editor で揃えるため、接続が完了してから `OnPlayerJoined` などが配信されます。
- どのオブジェクトがどれと同期するかは、VRChat 本番と同じ NetworkID の仕組みで対応付けているため、シーンが同じであれば Editor 間で必ず一致します。

## 同期されるもの

| 機能 | 対応 |
|---|---|
| `[UdonSynced]` 変数 (Continuous / Manual + `RequestSerialization`) | ✅ |
| `OnPreSerialization` / `OnPostSerialization` / `OnDeserialization` | ✅ |
| `SendCustomNetworkEvent`(引数付き `[NetworkCallable]` 含む、`NetworkCalling.CallingPlayer` 対応) | ✅ |
| Ownership (`Networking.SetOwner` / `TakeOwnership` / Pickup による移譲 / `OnOwnershipTransferred`) | ✅ |
| `VRCObjectSync`(位置・回転・Rigidbody) | ✅(10Hz、補間なし) |
| `VRCObjectPool` | ✅(同期変数として) |
| プレイヤーの参加/退出 (`OnPlayerJoined` / `OnPlayerLeft`)、マスター判定 | ✅ |
| プレイヤー位置の反映(リモートプレイヤーの移動) | ✅(10Hz) |
| Station 着席 (`OnStationEntered` / `OnStationExited`、リモートプレイヤーの着席表示) | ✅ |
| レイトジョイン(参加時に現在の同期状態を受信) | ✅ |
| ボイス、アバター、PlayerData/Persistence | ❌ 未対応 |
| `VRCInstantiate` した動的オブジェクトの同期 | ❌(VRChat 本番同様、ObjectPool を使ってください) |

## インストール

VPM パッケージ(`com.nyakomechan.vrchat-multisim`)として配布しています。

- **VRChat Package Managerで追加する場合**:
　(https://nyakomechan.github.io/vpm-repository/) よりAdd To VCCを押してリポジトリを登録、ManageProjectよりVRChatMuliSimを導入して下さい。
- **Unity Package Manager(git URL)で追加する場合**:
  `Window → Package Manager → + → Add package from git URL...` に以下を入力

  ```
  https://github.com/nyakomechan/VRChatMultiSim.git?path=/Packages/com.nyakomechan.vrchat-multisim
  ```

- **手動で追加する場合**: このリポジトリの `Packages/com.nyakomechan.vrchat-multisim/` フォルダを、
  ワールドプロジェクトの `Packages/` 直下にコピー(埋め込みパッケージ)

前提パッケージ:
- `com.vrchat.worlds` **3.10.x**(VCC ワールドプロジェクト)
- [ParrelSync](https://github.com/VeriorPies/ParrelSync)(git URL: `https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`)

## 使い方

1. **メニュー → Tools → VRChat MultiSim → Enable Multiplayer Simulation** をオンにする
   (EditorPrefs 保存なのでオリジナル/クローンの両方に効きます)
2. **Tools → VRChat MultiSim → Open ParrelSync Clones Manager** からクローンを作成し、クローンエディタを開く
3. **オリジナル側で ▶ Play**(ホストがポート 24685 で待受開始)
4. **クローン側で ▶ Play**(自動でホストに接続し、プレイヤーID 2 として参加)
5. 両方の Game ビューで動作確認。Console に `[MultiSim]` プレフィックスでログが出ます

- Play の順番は「ホストが先」推奨ですが、クライアントは最大 15 秒間リトライするので多少前後しても繋がります。
- ホストが見つからない場合は警告を出して**通常のシングルプレイ ClientSim として起動**します(ハングしません)。
- 3 人以上のテストはクローンを追加するだけです(Clones Manager から複数作成可)。

## 動作確認用サンプル

Package Manager でこのパッケージを選択し、**Samples タブ → "MultiSim Test Board" → Import** すると
UdonSharp のテストギミックが Assets 配下に取り込まれます。

- `MultiSimTestBoard.cs` — Cube 等に付けてクリック(Interact)→ Ownership 取得 + 同期変数
  インクリメント + `RequestSerialization` + 全員へネットワークイベント送信
- `MultiSimStationSeat.cs` — `VRCStation` 付きの椅子オブジェクトに付けてクリック → 着席。
  全エディタで `OnStationEntered/Exited` が発火し、リモート側でも着席位置に表示されます

各エディタの Console で `OnDeserialization` / NetworkEvent / Station イベントの受信を確認できます。

## 制限・注意

- **VRChat SDK (com.vrchat.worlds) 3.10.x 向け**です。ClientSim 内部にリフレクションでアクセスしているため、SDK 更新で動かなくなった場合は Console に「どのメンバーが見つからないか」がエラー表示されます。
- 同期は JSON/TCP による簡易実装です。実際の VRChat のような帯域制限・送信レート制御・補間は再現しません(Continuous は約 10Hz のポーリング)。
- Continuous オブジェクトの `OnPreSerialization` 内で行った変更は 1 tick 遅れて送信されます(Manual は正確です)。
- マスター移譲は「ホストが最初のマスター」を前提としています。ホスト(オリジナル)の Play を先に止めるとセッション終了扱いになり、クローンはシングルプレイ状態で継続します。
- リモートプレイヤーの見た目は ClientSim 標準のリモートプレイヤー(カプセル)で、位置・回転のみ反映されます(ボーン単位の姿勢は未対応)。
- Station 同期は「VRCStation がネットワークコンポーネント(UdonBehaviour 等)と同じ GameObject にある」ことが前提です(NetworkID でStationを特定するため)。着席ポーズは再現されず、リモートプレイヤーは着席位置に立ち姿で固定されます。
- ホストが自分のインスタンスオーナーになります。`isInstanceOwner` はホストのみ true です。
- ポートは `Tools/VRChat MultiSim` の設定(既定 24685)。他ツールと衝突する場合は `MultiSimPrefs` の EditorPrefs キー `MultiSim.Port` を変更してください。
- ポートは `Tools/VRChat MultiSim` の設定(既定 24685)。他ツールと衝突する場合は `MultiSimPrefs` の EditorPrefs キー `MultiSim.Port` を変更してください。
- Network ID Import and Export Utility で Regenerate Scene IDs を行った場合は、クローンプロジェクトの Unity Editor でも同様に Regenerate Scene IDs をしてください。

## ファイル構成

```
Packages/com.nyakomechan.vrchat-multisim/
  package.json          VPM/UPM パッケージ定義
  MultiSimCore.cs       起動制御・ハンドシェイク・ID再割当・Ready制御・同期ループ
  MultiSimTransport.cs  localhost TCP(長さプレフィックス付き JSON)
  MultiSimRegistry.cs   NetworkID → ClientSimNetworkIdHolder のマップ構築
  MultiSimReflect.cs    ClientSim 内部へのリフレクション橋渡し(SDK 3.10.x 固定)
  MultiSimUdonCodec.cs  UdonBehaviour 用 Encode/Decode(ClientSim のバグ回避)
  MultiSimJson.cs       VRCJson ベースのメッセージ/イベント引数シリアライズ
  MultiSimPrefs.cs      設定(EditorPrefs)+ ParrelSync 判別ヘルパー
  MultiSimMenu.cs       Tools メニュー
  Samples~/MultiSimTestBoard/
    MultiSimTestBoard.cs  動作確認用 UdonSharp ギミック(Samples から Import)
```

すべて `#if UNITY_EDITOR` で囲まれており、ワールドのビルド/アップロードには一切含まれません。

## LICENSE
MIT License

Copyright (c) nyakome

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
