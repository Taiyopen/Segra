import GameListManager from '../GameListManager';

export default function GameDetectionSection() {
  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <h2 className="text-xl font-semibold mb-2">Game Detection</h2>
      <p className="text-sm opacity-80 mb-4">
        Segra auto-detects most games. If a game isn't detected, add its executable to the Allow List. To
        prevent recording a game, add its executable to the Block List.
      </p>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="bg-base-200 p-4 rounded-lg border border-custom">
          <GameListManager listType="whitelist" />
        </div>
        <div className="bg-base-200 p-4 rounded-lg border border-custom">
          <GameListManager listType="blacklist" />
        </div>
      </div>
    </div>
  );
}
