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
      <div className="relative" ref={filterRef}>
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

        {isFilterOpen && (
          <div className="absolute right-0 mt-2 w-64 bg-base-300 shadow-lg rounded-box z-10 p-3 border border-base-content/20">
            <div className="flex justify-between items-center mb-2">
              <h3 className="font-medium">Filter by Game</h3>
              {selectedGames.length > 0 && (
                <button className="text-xs text-primary hover:underline" onClick={clearFilters}>
                  Clear all
                </button>
              )}
            </div>
            <div className="max-h-60 overflow-y-auto">
              {uniqueGames.length > 0 ? (
                uniqueGames.map((game) => (
                  <div key={game} className="form-control">
                    <label className="label cursor-pointer justify-start gap-2 py-1">
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
        )}
      </div>

      {/* Sort dropdown */}
      <div className="relative" ref={sortRef}>
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

        {isSortOpen && (
          <div className="absolute right-0 mt-2 w-48 bg-base-300 shadow-lg rounded-box z-10 border border-base-content/20">
            <ul className="menu p-2">
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "newest" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => handleSortChange("newest")}
                >
                  <MdOutlineAccessTime className="text-lg" /> Newest
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "oldest" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => handleSortChange("oldest")}
                >
                  <MdOutlineAccessTime className="text-lg" /> Oldest
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "size" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => handleSortChange("size")}
                >
                  <MdOutlineStorage className="text-lg" /> Size
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "duration" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => handleSortChange("duration")}
                >
                  <MdOutlineTimer className="text-lg" /> Duration
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "game" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => handleSortChange("game")}
                >
                  <MdOutlineGamepad className="text-lg" /> Game A–Z
                </button>
              </li>
            </ul>
          </div>
        )}
      </div>
    </div>
  );
}
