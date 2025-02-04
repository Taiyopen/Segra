/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{vue,js,ts,tsx}'],
  theme: {
    extend: {},
  },
  daisyui: {
    themes: [
      "light",
      "dark",
      "cupcake",
      "bumblebee",
      "emerald",
      "corporate",
      "synthwave",
      "retro",
      "cyberpunk",
      "valentine",
      "halloween",
      "garden",
      "forest",
      "aqua",
      "lofi",
      "pastel",
      "fantasy",
      "wireframe",
      "black",
      "luxury",
      "dracula",
      "cmyk",
      "autumn",
      "business",
      "acid",
      "lemonade",
      "night",
      "coffee",
      "winter",
      "dim",
      "nord",
      "sunset",
      {
        segra: {
          primary: "#14B3B2", // Gold
          secondary: "#25272E", // Druid
          accent: "#E85A5A", // Poppy
          neutral: "#31333E", // Obsidian
          "base-100": "#31333E", // Necromancer (Background)
          "base-200": "#DBDAE1", // Powder
          "base-300": "#25272E", // Salt
          "base-content": "#F2F3F7", // Text color for base backgrounds
          info: "#578DDD", // Soldier
          success: "#14B3B2", // Druid (reuse for success)
          warning: "#EFAF2B", // Gold (reuse for warning)
          error: "#E85A5A", // Poppy (reuse for error)
        },
        rich: {
          primary: "#116860", // Deep teal green
          secondary: "#0b423d", // Dark forest green
          accent: "#E85A5A", // Vibrant coral red
          neutral: "#09413c", // Dark greenish teal
          "base-100": "#132625", // Very dark, muted green
          "base-200": "#DBDAE1", // Soft lavender-gray
          "base-300": "#0c1919", // Near-black with green undertones
          "base-content": "#F2F3F7", // Very light grayish white
          info: "#578DDD", // Medium sky blue
          success: "#14B3B2", // Bright aqua turquoise
          warning: "#EFAF2B", // Rich mustard yellow
          error: "#E85A5A", // Bold coral red
        },
      },
    ],
  },
  plugins: [require('daisyui')],
}