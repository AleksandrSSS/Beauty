import { Link } from 'react-router-dom'
import MasterPhoto from './MasterPhoto'
import type { MasterDto } from '../types'

/** Клікабельна картка майстра на головній. Корінь — <Link>, не <div onClick>. */
export default function MasterCard({ m }: { m: MasterDto }) {
  return (
    <Link to={`/masters/${m.id}`} className="card master-card">
      <MasterPhoto name={m.name} photoUrl={m.photoUrl} />
      <h3>{m.name}</h3>
      <p>{m.specialty}</p>
      <p className="muted">{m.bio}</p>
      <p className="muted">
        {m.rotationType === 'TwoTwo'
          ? (m.worksToday ? '✅ На зміні сьогодні' : m.worksTomorrow ? '🕘 На зміні з завтра' : '🏖 Зараз вихідні (2/2)')
          : <>
              {m.worksThisWeek ? '✅ Працює цього тижня' : '🏖 Відпочиває цього тижня'}
              {' · '}
              {m.worksNextWeek ? '✅ працює наступного' : '🏖 відпочиває наступного'}
            </>}
      </p>
      <span className="master-card__cta">Графік і запис →</span>
    </Link>
  )
}