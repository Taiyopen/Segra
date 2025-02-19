/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{vue,js,ts,tsx}'],
  theme: {
    extend: {},
  },
  daisyui: {
    themes: [
      {
        segra: {
          "primary": "#fecb00",    // Sweden Yellow - Primary brand color
          "secondary": "#171f2a",  // Dawn Blue - Secondary accent
          "accent": "#fecb00",     // Sweden Blue - Accent color
          "neutral": "#142638",    // Dark Dawn Blue - Neutral dark shade
          "base-100": "#132733",   // Dark Dawn Blue - Main background
          "base-200": "#15232F",   // Darker shade - Secondary background
          "base-300": "#171f2a",   // Darker shade - Tertiary background
          "base-400": "#49515b",    // Winter Grey - Neutral light shade
          "base-content": "#ffffff", // White - Text color
          "info": "#005293",       // Sweden Blue - Informational color
          "success": "#36D399",    // Green - Success state
          "warning": "#ffd481",    // Pale Yellow - Warning state
          "error": "#F87272",      // Red - Error state
        },
      },
    ],
  },
  plugins: [require('daisyui')],
}