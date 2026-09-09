import { useState } from 'react'
import { resolvePhotoUrl } from '../api'

/**
 * Фото майстра з fallback на ініціали. Спільний для картки на головній
 * (grid, ширина 100%) і для шапки сторінки майстра (задається контекстом,
 * напр. `.master-head .master-photo { width: 160px }`).
 */
export default function MasterPhoto({
  name,
  photoUrl,
  className
}: {
  name: string
  photoUrl?: string | null
  className?: string
}) {
  const [failed, setFailed] = useState(false)
  const photo = photoUrl ? resolvePhotoUrl(photoUrl) : null
  const cls = ['master-photo', className].filter(Boolean).join(' ')

  if (photo && !failed) {
    return (
      <img
        className={cls}
        src={photo}
        alt={`Фото майстра ${name}`}
        loading="lazy"
        width={240}
        height={240}
        onError={() => setFailed(true)}
      />
    )
  }
  // Декоративний fallback: ім'я вже є текстом поруч.
  return (
    <div className={`${cls} master-photo--fallback`} aria-hidden="true">
      {name.trim().charAt(0).toUpperCase()}
    </div>
  )
}