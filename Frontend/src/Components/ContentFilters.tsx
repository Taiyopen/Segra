import React, { useState, useEffect, useRef } from "react";
import { Content } from "../Models/types";
import { MdFilterList, MdSort, MdOutlineAccessTime, MdOutlineStorage, MdOutlineTimer, MdOutlineGamepad } from "react-icons/md";

export type SortOption = "newest" | "oldest" | "size" | "duration" | "game";
const SORT_OPTIONS: SortOption[] = ["newest", "oldest", "size", "duration", "game"];

export interface ContentFiltersProps {
  items: Content[];
  onFilteredItemsChange: (filteredItems: Content[]) => void;
  sectionId: string;
}

export default function ContentFilters({
  items,
  onFilteredItemsChange,
  sectionId,
}: ContentFiltersProps) {
  // Persisted initializers
  const [selectedGames, setSelectedGames] = useState<string[]>(() => {
    try {
      const saved = localStorage.getItem(`${sectionId}-filters`);
      const parsed = saved ? JSON.parse(saved) : [];
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  });

  const [sortOption, setSortOption] = useState<SortOption>(() => {
    try {
      const saved = localStorage.getItem(`${sectionId}-sort`);
      const parsed = saved ? JSON.parse(saved) : "newest";
      return SORT_OPTIONS.includes(parsed) ? parsed : "newest";
    } catch {
      return "newest";
    }
  });

  const [isFilterOpen, setIsFilterOpen] = useState(false);
  const [isSortOpen, setIsSortOpen] = useState(false);

  const filterRef = useRef<HTMLDivElement>(null);
  const sortRef = useRef<HTMLDivElement>(null);

  // Keep latest callback reference
  const callbackRef = useRef(onFilteredItemsChange);
  useEffect(() => {
    callbackRef.current = onFilteredItemsChange;
  }, [onFilteredItemsChange]);

  // Unique game list
  const uniqueGames = React.useMemo(() => {
    const games = items.map((item) => item.game);
    return [...new Set(games)].sort();
  }, [items]);

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

  // Load persisted state when sectionId changes
  useEffect(() => {
    try {
      const savedFilters = localStorage.getItem(`${sectionId}-filters`);
      const savedSort = localStorage.getItem(`${sectionId}-sort`);

      if (savedFilters) {
        const parsedFilters = JSON.parse(savedFilters);
        setSelectedGames(Array.isArray(parsedFilters) ? parsedFilters : []);
      } else {
        setSelectedGames([]);
      }

      if (savedSort) {
        const parsedSort = JSON.parse(savedSort);
        setSortOption(SORT_OPTIONS.includes(parsedSort) ? parsedSort : "newest");
      } else {
        setSortOption("newest");
      }
    } catch {
      setSelectedGames([]);
      setSortOption("newest");
    }
  }, [sectionId]);

  // Persist changes
  useEffect(() => {
    try {
      localStorage.setItem(`${sectionId}-filters`, JSON.stringify(selectedGames));
      localStorage.setItem(`${sectionId}-sort`, JSON.stringify(sortOption));
    } catch {
      /* no-op */
    }
  }, [selectedGames, sortOption, sectionId]);

  // Compute filtered & sorted results
  const filteredResults = React.useMemo(() => {
    let out = [...items];

    if (selectedGames.length > 0) {
      out = out.filter((item) => selectedGames.includes(item.game));
    }

    out.sort((a, b) => {
      switch (sortOption) {
        case "newest":
          return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
        case "oldest":
          return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
        case "size":
          return (b.fileSizeKb ?? 0) - (a.fileSizeKb ?? 0);
        case "duration": {
          const toSecs = (dur: string) =>
            dur.split(":").reduce((acc, t) => 60 * acc + (parseInt(t, 10) || 0), 0);
          return toSecs(b.duration) - toSecs(a.duration);
        }
        case "game": {
          const byGame = a.game.localeCompare(b.game);
          return byGame !== 0 ? byGame : a.title.localeCompare(b.title);
        }
        default:
          return 0;
      }
    });

    return out;
  }, [items, selectedGames, sortOption]);

  // Notify parent only when results actually change
  const prevSigRef = React.useRef<string>("");
  const makeSig = (arr: Content[]) =>
    arr.map((i) => `${i.title}|${i.createdAt}|${i.game}`).join("||");

  useEffect(() => {
    const nextSig = makeSig(filteredResults);
    if (prevSigRef.current !== nextSig) {
      prevSigRef.current = nextSig;
      callbackRef.current(filteredResults);
    }
  }, [filteredResults]);

  // UI handlers
  const toggleGameSelection = (game: string) => {
    setSelectedGames((prev) => (prev.includes(game) ? prev.filter((g) => g !== game) : [...prev, game]));
  };

  const clearFilters = () => setSelectedGames([]);

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
                  onClick={() => {
                    setSortOption("newest");
                    setIsSortOpen(false);
                  }}
                >
                  <MdOutlineAccessTime className="text-lg" /> Newest
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "oldest" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => {
                    setSortOption("oldest");
                    setIsSortOpen(false);
                  }}
                >
                  <MdOutlineAccessTime className="text-lg" /> Oldest
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "size" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => {
                    setSortOption("size");
                    setIsSortOpen(false);
                  }}
                >
                  <MdOutlineStorage className="text-lg" /> Size
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "duration" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => {
                    setSortOption("duration");
                    setIsSortOpen(false);
                  }}
                >
                  <MdOutlineTimer className="text-lg" /> Duration
                </button>
              </li>
              <li>
                <button
                  className={`hover:text-primary hover:bg-base-200 ${sortOption === "game" ? "text-primary" : ""} flex items-center gap-1`}
                  onClick={() => {
                    setSortOption("game");
                    setIsSortOpen(false);
                  }}
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
