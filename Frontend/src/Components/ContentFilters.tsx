import { useState, useEffect, useRef } from "react";
import { MdFilterList, MdSort, MdOutlineAccessTime, MdOutlineStorage, MdOutlineTimer, MdOutlineGamepad } from "react-icons/md";

export type SortOption = "newest" | "oldest" | "size" | "duration" | "game";

export interface ContentFiltersProps {
  uniqueGames: string[];
  onGameFilterChange: (selectedGames: string[]) => void;
  onSortChange: (sortOption: SortOption) => void;
  sectionId: string;
  selectedGames: string[];
  sortOption: SortOption;
}

export default function ContentFilters({
  uniqueGames,
  onGameFilterChange,
  onSortChange,
  sectionId,
  selectedGames,
  sortOption,
}: ContentFiltersProps) {
  const [isFilterOpen, setIsFilterOpen] = useState(false);
  const [isSortOpen, setIsSortOpen] = useState(false);

  const filterRef = useRef<HTMLDivElement>(null);
  const sortRef = useRef<HTMLDivElement>(null);

  // Close dropdowns on outside click
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (filterRef.current && !filterRef.current.contains(event.target as Node) && isFilterOpen) {
        setIsFilterOpen(false);
      }
      if (sortRef.current && !sortRef.current.contains(event.target as Node) && isSortOpen) {
        setIsSortOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [isFilterOpen, isSortOpen]);

  // Persist changes to localStorage
  useEffect(() => {
    try {
      localStorage.setItem(`${sectionId}-filters`, JSON.stringify(selectedGames));
      localStorage.setItem(`${sectionId}-sort`, JSON.stringify(sortOption));
    } catch {
      /* no-op */
    }
  }, [selectedGames, sortOption, sectionId]);
  
  // UI handlers
  const toggleGameSelection = (game: string) => {
    const newSelectedGames = selectedGames.includes(game) 
      ? selectedGames.filter((g: string) => g !== game) 
      : [...selectedGames, game];
    onGameFilterChange(newSelectedGames);
  };

  const clearFilters = () => {
    onGameFilterChange([]);
  };

  const handleSortChange = (option: SortOption) => {
    onSortChange(option);
    setIsSortOpen(false);
  };

  const getSortLabel = (option: SortOption): string => {
    switch (option) {
      case "newest":
        return "Newest";
      case "oldest":
        return "Oldest";
      case "size":
        return "Size";
      case "duration":
        return "Duration";
      case "game":
        return "Game";
    }
  };

  return (
    <div className="flex items-center space-x-2">
      {/* Filter dropdown */}
      <div className={`dropdown dropdown-end ${isFilterOpen ? 'dropdown-open' : ''}`} ref={filterRef}>
        <button
          className="btn btn-sm no-animation btn-secondary border border-primary hover:text-primary hover:border-primary flex items-center gap-1"
          onClick={() => {
            setIsFilterOpen(!isFilterOpen);
            setIsSortOpen(false);
          }}
        >
          <MdFilterList />
          Filter
          {selectedGames.length > 0 && (
            <span className="badge badge-sm badge-primary">{selectedGames.length}</span>
          )}
        </button>
        <div className="dropdown-content bg-base-300 border border-primary rounded-box z-[999] w-64 p-3 mt-1 shadow" tabIndex={0}>
          <div className="flex justify-between items-center mb-1">
            <h3 className="font-medium">Filter by Game</h3>
            {selectedGames.length > 0 && (
              <button className="text-xs text-primary hover:underline" onClick={clearFilters}>
                Clear all
              </button>
            )}
          </div>
          <div className="max-h-60 overflow-y-auto mt-1 border-t border-base-content/10 pt-1">
            {uniqueGames.length > 0 ? (
              uniqueGames.map((game) => (
                <div key={game} className="form-control">
                  <label className="cursor-pointer flex w-full items-center justify-start gap-2 px-3 py-2.5 text-white hover:bg-white/5 active:bg-base-200/20 rounded-lg transition-all duration-200 hover:pl-4 outline-none">
                    <input
                      type="checkbox"
                      className="checkbox checkbox-sm checkbox-primary"
                      checked={selectedGames.includes(game)}
                      onChange={() => toggleGameSelection(game)}
                    />
                    <span className="label-text">{game}</span>
                  </label>
                </div>
              ))
            ) : (
              <p className="text-sm text-base-content/70">No games available</p>
            )}
          </div>
        </div>
      </div>

      {/* Sort dropdown */}
      <div className={`dropdown dropdown-end ${isSortOpen ? 'dropdown-open' : ''}`} ref={sortRef}>
        <button
          className="btn btn-sm no-animation btn-secondary border border-primary hover:text-primary hover:border-primary flex items-center gap-1"
          onClick={() => {
            setIsSortOpen(!isSortOpen);
            setIsFilterOpen(false);
          }}
        >
          <MdSort />
          {getSortLabel(sortOption)}
        </button>
        <ul className="dropdown-content menu bg-base-300 border border-primary rounded-box z-[999] w-56 p-2 mt-1 shadow" tabIndex={0}>
          <li>
            <a
              className={`flex w-full items-center gap-2 px-4 py-3 ${
                sortOption === "newest" ? "text-primary" : "text-white"
              } hover:bg-white/5 active:!text-primary active:!bg-white/5 rounded-lg transition-all duration-200 hover:pl-5 outline-none`}
              onClick={() => handleSortChange("newest")}
            >
              <MdOutlineAccessTime size="20" />
              <span>Newest</span>
            </a>
          </li>
          <li>
            <a
              className={`flex w-full items-center gap-2 px-4 py-3 ${
                sortOption === "oldest" ? "text-primary" : "text-white"
              } hover:bg-white/5 active:!text-primary active:!bg-white/5 rounded-lg transition-all duration-200 hover:pl-5 outline-none`}
              onClick={() => handleSortChange("oldest")}
            >
              <MdOutlineAccessTime size="20" />
              <span>Oldest</span>
            </a>
          </li>
          <li>
            <a
              className={`flex w-full items-center gap-2 px-4 py-3 ${
                sortOption === "size" ? "text-primary" : "text-white"
              } hover:bg-white/5 active:!text-primary active:!bg-white/5 rounded-lg transition-all duration-200 hover:pl-5 outline-none`}
              onClick={() => handleSortChange("size")}
            >
              <MdOutlineStorage size="20" />
              <span>Size</span>
            </a>
          </li>
          <li>
            <a
              className={`flex w-full items-center gap-2 px-4 py-3 ${
                sortOption === "duration" ? "text-primary" : "text-white"
              } hover:bg-white/5 active:!text-primary active:!bg-white/5 rounded-lg transition-all duration-200 hover:pl-5 outline-none`}
              onClick={() => handleSortChange("duration")}
            >
              <MdOutlineTimer size="20" />
              <span>Duration</span>
            </a>
          </li>
          <li>
            <a
              className={`flex w-full items-center gap-2 px-4 py-3 ${
                sortOption === "game" ? "text-primary" : "text-white"
              } hover:bg-white/5 active:!text-primary active:!bg-white/5 rounded-lg transition-all duration-200 hover:pl-5 outline-none`}
              onClick={() => handleSortChange("game")}
            >
              <MdOutlineGamepad size="20" />
              <span>Game A–Z</span>
            </a>
          </li>
        </ul>
      </div>
    </div>
  );
}
