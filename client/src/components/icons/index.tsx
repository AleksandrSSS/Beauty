import type { SVGProps } from 'react'
import { Children } from 'react'
import type { ReactNode } from 'react'

/**
 * Мінімалистична SVG-иконки власної роботи (без lucide-react).
 * Усі — stroke-иконки, декоративні: у <svg> заданий aria-hidden,
 * а смисловый доступ лише через сусідні текстові лейбли.
 */
type P = SVGProps<SVGSVGElement>

function base(children: ReactNode[], className = '') {
  return (
    <svg
      viewBox="0 0 24 24"
      width="24"
      height="24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
    >
      {Children.toArray(children)}
    </svg>
  )
}

export const IconScissors = (p: P) => base([
  <path d="M8 5 L8 19" />,
  <path d="M6 19 Q3 28 2 24" />,
  <path d="M16 5 L16 19" />,
  <path d="M18 19 Q21 28 22 24" />,
  <path d="M9 13 L15 13" />
], p.className ?? '')

export const IconNail = (p: P) => base([
  <ellipse cx="12" cy="16" rx="7" ry="5" />,
  <path d="M7 3 L10 7 L15 7 L19 3 M14 6 L15 7" />,
  <path d="M9 9 L9 12 M15 9 L15 12" />,
  <path d="M11 7 L11 9 M13 7 L13 9" />,
  <path d="M12 10 L12 12" />
], p.className ?? '')

export const IconPalette = (p: P) => base([
  <path d="M6 6 h12 v-2 h3 v6 h-3 v5 h-1" />,
  <circle cx="9" cy="10" r="1.5" />,
  <circle cx="13" cy="10" r="1.5" />,
  <circle cx="17" cy="10" r="1.5" />,
  <path d="M7 16 L9 14 M15 4 L18 6" />
], p.className ?? '')

export const IconHeart = (p: P) => base([
  <path d="M12 7 C10 5 11 3 12 17 L12 21 M12 7 C14 5 13 3 13 17 L12 21" />,
  <path d="M10 20 L14 20" />
], p.className ?? '')

export const IconMapPin = (p: P) => base([
  <path d="M8 20 h8 M11 18 L11 8 M11 8 C11 7 11 6 10 5 L9 5" />,
  <path d="M9 5 L9 4" />
], p.className ?? '')

export const IconPhone = (p: P) => base([
  <path d="M5 4 C8 2 12 4 17 6 L20 6" />,
  <path d="M5 6 L5 19 M5 19 L19 19 M19 19 L19 6" />,
  <path d="M8 9 L8 13 M16 9 L16 13" />
], p.className ?? '')

export const IconClock = (p: P) => base([
  <circle cx="12" cy="12" r="9" />,
  <path d="M12 12 L16 7 M12 12 L7 14" />
], p.className ?? '')

export const IconMail = (p: P) => base([
  <rect x="4" y="6" width="16" height="13" rx="2" />,
  <path d="M12 6 L3 13 M6 6 L21 13" />
], p.className ?? '')

export const IconMenu = (p: P) => base([
  <path d="M4 6 H20 M4 12 H20 M4 18 H20" />
], p.className ?? '')

export const IconInstagram = (p: P) => base([
  <rect x="4" y="4" width="16" height="16" rx="5" />,
  <circle cx="12" cy="12" r="4" />
], p.className ?? '')

export const IconTelegram = (p: P) => base([
  <path d="M7 5 H17 M7 19 H17" />,
  <path d="M11 7 C10 10 9 13 8 15 L8 19 M8 19 L6 21" />,
  <path d="M5 19 L8 19" />
], p.className ?? '')

export const IconFacebook = (p: P) => base([
  <path d="M5 5 H19 M19 5 L5 20 H19 z" />,
  <path d="M7 6 H17 M12 12 L12 20 M7 10 H17" />
], p.className ?? '')

export const IconArrow = (p: P) => base([
  <path d="M4 13 H21 M21 13 L14 5" />
], p.className ?? '')

export const IconFlower = (p: P) => base([
  <circle cx="12" cy="12" r="3" />,
  <path d="M12 7 C10 7 9 8 8 9 M12 7 C14 7 15 8 16 9" />,
  <path d="M12 12 C9 13 7 15 5 17 M12 12 C15 13 17 15 19 17" />,
  <path d="M12 9 L12 15" />
], p.className ?? '')

/**
 * Кольоровий логотип-квітка салону (заливка, не stroke).
 * Винесено з App.tsx (блок logo-flower).
 */
export const IconFlowerDef = (p: P) => (
  <svg
    viewBox="0 0 100 100"
    className={p.className ?? ''}
    aria-hidden="true"
  >
    <g transform="translate(0, -2)">
      <path d="M50 60 C20 45 0 75 25 95 C35 85 45 75 50 60 Z" fill="#43A047" stroke="#2E7D32" strokeWidth="2" strokeLinejoin="round" />
      <path d="M50 60 C80 45 100 75 75 95 C65 85 55 75 50 60 Z" fill="#43A047" stroke="#2E7D32" strokeWidth="2" strokeLinejoin="round" />
      <circle cx="36" cy="36" r="18" fill="#5E35B1" stroke="#4527A0" strokeWidth="1.5" />
      <circle cx="64" cy="36" r="18" fill="#5E35B1" stroke="#4527A0" strokeWidth="1.5" />
      <circle cx="25" cy="58" r="18" fill="#7E57C2" stroke="#5E35B1" strokeWidth="1.5" />
      <circle cx="75" cy="58" r="18" fill="#7E57C2" stroke="#5E35B1" strokeWidth="1.5" />
      <path d="M30 62 C30 95 70 95 70 62 Z" fill="#9575CD" stroke="#7E57C2" strokeWidth="1.5" />
      <polygon points="42,58 50,46 58,58" fill="#FFF8E1" />
      <circle cx="50" cy="56" r="5" fill="#FFCA28" />
      <circle cx="50" cy="56" r="2.5" fill="#FF8F00" />
      <path d="M50 63 L50 76 M45 61 L38 71 M55 61 L62 71" stroke="#FFF8E1" strokeWidth="1.5" strokeLinecap="round" />
    </g>
  </svg>
)