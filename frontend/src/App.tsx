import { FormEvent, useEffect, useState } from "react";
import { createHold, getClasses, releaseHold } from "./api";
import type { Hold, SchoolClass } from "./types";

export default function App() {
  const [classes, setClasses] = useState<SchoolClass[]>([]);
  const [selected, setSelected] = useState<SchoolClass | null>(null);
  const [studentName, setStudentName] = useState("");
  const [hold, setHold] = useState<Hold | null>(null);
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(true);

  async function refresh() {
    setLoading(true);
    try { setClasses(await getClasses()); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Unable to load classes."); }
    finally { setLoading(false); }
  }
  useEffect(() => { void refresh(); }, []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!selected || !studentName.trim()) return;
    try {
      setHold(await createHold(selected.id, studentName.trim()));
      setMessage("Seat held successfully. Complete admission before it expires.");
      setStudentName("");
      await refresh();
    } catch (error) { setMessage(error instanceof Error ? error.message : "Unable to hold the seat."); }
  }
  async function release() {
    if (!hold) return;
    try {
      await releaseHold(hold.id); setHold(null); setSelected(null); setMessage("Seat released."); await refresh();
    } catch (error) { setMessage(error instanceof Error ? error.message : "Unable to release the seat."); }
  }

  return <main>
    <header><p className="eyebrow">Admissions 2026</p><h1>Find your next classroom.</h1><p>Choose a class and reserve a seat while you complete your admission.</p></header>
    {message && <div className="notice" role="status">{message}</div>}
    {hold ? <section className="card confirmation"><h2>Seat reserved</h2><p><strong>{hold.studentName}</strong> has a temporary seat in <strong>{hold.className}</strong>.</p><p>Reservation expires at {new Date(hold.expiresAt).toLocaleTimeString()}.</p><button className="secondary" onClick={() => void release()}>Release seat</button></section> :
      <section><h2>Available classes</h2>{loading ? <p>Loading classes...</p> : <div className="grid">{classes.map(item =>
        <button className={`card class-card ${selected?.id === item.id ? "selected" : ""}`} key={item.id} disabled={item.availableSeats < 1} onClick={() => setSelected(item)}>
          <span className="grade">{item.grade}</span><h3>{item.name}</h3><span>{item.availableSeats} of {item.capacity} seats available</span>
        </button>)}</div>}
        {selected && <form className="card form" onSubmit={submit}><h2>Reserve {selected.name}</h2><label htmlFor="student-name">Student name</label><input id="student-name" value={studentName} onChange={event => setStudentName(event.target.value)} minLength={2} maxLength={100} required placeholder="Enter the student's full name" /><button type="submit">Hold available seat</button></form>}
      </section>}
  </main>;
}

