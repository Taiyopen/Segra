/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{vue,js,ts,tsx}'],
  theme: {
    extend: {
      borderColor: {
        custom: '#2e3640',
        primary: '#49515b',
        primaryYellow: '#fecb00',
      },
      outlineColor: {
        custom: '#2e3640',
        primary: '#49515b',
        primaryYellow: '#fecb00',
      },
    },
  }
}