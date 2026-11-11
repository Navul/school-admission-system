import React, { useEffect, useState, useCallback } from 'react';
import { api } from './api';
import { HoldStatus, MyHoldItem, SchoolClassSummary } from './types';

const STORAGE_STUDENT_ID_KEY = 'admission_student_id';
const STORAGE_ACTIVE_HOLD_KEY = 'admission_active_hold_id';

function isValidBirthRegNumber(id: string): boolean {
  return /^\d{17}$/.test(id.trim());
}

export default function App() {
  const [studentId, setStudentId] = useState<string>(() => {
    return localStorage.getItem(STORAGE_STUDENT_ID_KEY) || '';
  });

  const [classes, setClasses] = useState<SchoolClassSummary[]>([]);
  const [myHolds, setMyHolds] = useState<MyHoldItem[]>([]);
  const [selectedHoldId, setSelectedHoldId] = useState<string | null>(() => {
    return localStorage.getItem(STORAGE_ACTIVE_HOLD_KEY);
  });

  // Admin Create Class form state
  const [newClassName, setNewClassName] = useState('');
  const [newClassSeats, setNewClassSeats] = useState('120');
  const [isCreatingClass, setIsCreatingClass] = useState(false);

  // Action loading & feedback banners
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // 1-second tick for real-time countdown & instant client-side expiry detection
  const [nowMs, setNowMs] = useState<number>(() => Date.now());

  useEffect(() => {
    const timer = setInterval(() => {
      setNowMs(Date.now());
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  // Persist studentId in localStorage
  const handleStudentIdChange = (value: string) => {
    const digitsOnly = value.replace(/\D/g, '').slice(0, 17);
    setStudentId(digitsOnly);
    localStorage.setItem(STORAGE_STUDENT_ID_KEY, digitsOnly);
    setErrorMsg(null);
  };

  // Select active hold and save in localStorage
  const selectHold = (holdId: string | null) => {
    setSelectedHoldId(holdId);
    if (holdId) {
      localStorage.setItem(STORAGE_ACTIVE_HOLD_KEY, holdId);
    } else {
      localStorage.removeItem(STORAGE_ACTIVE_HOLD_KEY);
    }
  };

  const fetchClasses = useCallback(async () => {
    try {
      const list = await api.getClasses();
      setClasses(list);
    } catch (err: any) {
      setErrorMsg(err.message || 'ক্লাসের তালিকা লোড করতে সমস্যা হয়েছে।');
    }
  }, []);

  const fetchStudentHolds = useCallback(async (currentStudentId: string) => {
    if (!isValidBirthRegNumber(currentStudentId)) {
      setMyHolds([]);
      return;
    }
    try {
      const holds = await api.getMyHolds(currentStudentId);
      setMyHolds(holds);

      // Auto-select an active HELD item if none is currently selected
      setSelectedHoldId((prevId) => {
        if (prevId && holds.some((h) => h.holdId === prevId)) {
          return prevId;
        }
        const active = holds.find((h) => h.status === 'HELD') || holds[0];
        const nextId = active ? active.holdId : null;
        if (nextId) {
          localStorage.setItem(STORAGE_ACTIVE_HOLD_KEY, nextId);
        } else {
          localStorage.removeItem(STORAGE_ACTIVE_HOLD_KEY);
        }
        return nextId;
      });
    } catch (err: any) {
      setErrorMsg(err.message || 'আপনার আবেদনের তথ্য লোড করা যায়নি।');
    }
  }, []);

  // Poll classes and student holds every 2.5 seconds without page reload
  useEffect(() => {
    fetchClasses();
    if (isValidBirthRegNumber(studentId)) {
      fetchStudentHolds(studentId);
    } else {
      setMyHolds([]);
    }

    const interval = setInterval(() => {
      fetchClasses();
      if (isValidBirthRegNumber(studentId)) {
        fetchStudentHolds(studentId);
      }
    }, 2500);

    return () => clearInterval(interval);
  }, [studentId, fetchClasses, fetchStudentHolds]);

  // Compute effective status (auto-transitions HELD -> EXPIRED immediately when timer hits 0)
  const getEffectiveStatus = (hold: MyHoldItem): HoldStatus => {
    if (hold.status === 'HELD') {
      const expiryTime = new Date(hold.expiresAt).getTime();
      if (expiryTime <= nowMs) {
        return 'EXPIRED';
      }
    }
    return hold.status;
  };

  const getRemainingSeconds = (expiresAtIso: string): number => {
    const diff = Math.floor((new Date(expiresAtIso).getTime() - nowMs) / 1000);
    return Math.max(0, diff);
  };

  const formatCountdown = (seconds: number): string => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
  };

  // 1. Admin: Create Class
  const handleCreateClass = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMsg(null);
    setSuccessMsg(null);

    const trimmedName = newClassName.trim();
    const seats = Number(newClassSeats);

    if (!trimmedName) {
      setErrorMsg('অনুগ্রহ করে ক্লাসের নাম লিখুন (যেমন: Class 6 - Morning Shift)।');
      return;
    }
    if (!Number.isInteger(seats) || seats <= 0) {
      setErrorMsg('মোট আসন সংখ্যা অন্তত ১ বা তার বেশি পূর্ণসংখ্যা হতে হবে।');
      return;
    }

    setIsCreatingClass(true);
    try {
      await api.createClass({ name: trimmedName, totalSeats: seats });
      setNewClassName('');
      setNewClassSeats('120');
      setSuccessMsg(`"${trimmedName}" সফলভাবে তৈরি হয়েছে।`);
      await fetchClasses();
    } catch (err: any) {
      setErrorMsg(err.message);
    } finally {
      setIsCreatingClass(false);
    }
  };

  // 2. Parent/Student: Apply (Hold a Seat)
  const handleApply = async (schoolClass: SchoolClassSummary) => {
    setErrorMsg(null);
    setSuccessMsg(null);

    if (!isValidBirthRegNumber(studentId)) {
      setErrorMsg('আবেদন করার পূর্বে উপরে শিক্ষার্থীর ১৭ অঙ্কের সঠিক জন্ম নিবন্ধন নম্বর লিখুন।');
      return;
    }

    setBusyAction(`apply_${schoolClass.id}`);
    try {
      const created = await api.createHold(schoolClass.id, studentId);
      selectHold(created.holdId);
      setSuccessMsg(`"${schoolClass.name}"-এ আপনার আসন সাময়িকভাবে সংরক্ষিত (HELD) হয়েছে। সময় শেষ হওয়ার আগেই ভর্তি ফি জমা দিন।`);
      await Promise.all([fetchClasses(), fetchStudentHolds(studentId)]);
    } catch (err: any) {
      setErrorMsg(err.message);
      await fetchClasses();
    } finally {
      setBusyAction(null);
    }
  };

  // 3. Confirm Admission ("ভর্তি ফি দিন")
  const handleConfirmHold = async (holdId: string) => {
    setErrorMsg(null);
    setSuccessMsg(null);

    if (!isValidBirthRegNumber(studentId)) {
      setErrorMsg('১৭ অঙ্কের জন্ম নিবন্ধন নম্বর সঠিক নয়।');
      return;
    }

    setBusyAction(`confirm_${holdId}`);
    try {
      await api.confirmHold(holdId, studentId);
      setSuccessMsg('ভর্তি ফি জমা সফল হয়েছে! আপনার ভর্তি নিশ্চিত (CONFIRMED) করা হয়েছে।');
      await Promise.all([fetchClasses(), fetchStudentHolds(studentId)]);
    } catch (err: any) {
      setErrorMsg(err.message);
      await Promise.all([fetchClasses(), fetchStudentHolds(studentId)]);
    } finally {
      setBusyAction(null);
    }
  };

  // 4. Cancel Hold ("বাতিল")
  const handleCancelHold = async (holdId: string) => {
    setErrorMsg(null);
    setSuccessMsg(null);

    if (!isValidBirthRegNumber(studentId)) {
      setErrorMsg('১৭ অঙ্কের জন্ম নিবন্ধন নম্বর সঠিক নয়।');
      return;
    }

    setBusyAction(`cancel_${holdId}`);
    try {
      await api.cancelHold(holdId, studentId);
      setSuccessMsg('আবেদনটি বাতিল (CANCELLED) করা হয়েছে এবং আসনটি ছেড়ে দেওয়া হয়েছে।');
      await Promise.all([fetchClasses(), fetchStudentHolds(studentId)]);
    } catch (err: any) {
      setErrorMsg(err.message);
      await Promise.all([fetchClasses(), fetchStudentHolds(studentId)]);
    } finally {
      setBusyAction(null);
    }
  };

  const activeHold = myHolds.find((h) => h.holdId === selectedHoldId) || myHolds[0] || null;

  return (
    <div className="app-container">
      {/* Top Header & Student 17-Digit Birth Registration Input */}
      <header className="top-bar">
        <div>
          <h1>অনলাইন স্কুল ভর্তি সিস্টেম (School Admission System)</h1>
          <p className="subtitle">আগে আবেদন করলে আগে পাবেন ভিত্তিতে আসন সংরক্ষণ ও ভর্তি নিশ্চিতকরণ</p>
        </div>

        <div className="student-id-box">
          <label htmlFor="student-id-input">
            শিক্ষার্থীর জন্ম নিবন্ধন নম্বর (১৭ অঙ্ক):
          </label>
          <input
            id="student-id-input"
            type="text"
            inputMode="numeric"
            placeholder="যেমন: 20152691234567890"
            value={studentId}
            onChange={(e) => handleStudentIdChange(e.target.value)}
          />
          <span className={`id-status ${isValidBirthRegNumber(studentId) ? 'valid' : 'invalid'}`}>
            {studentId.length === 0
              ? '১৭ অঙ্কের নম্বর দিন'
              : isValidBirthRegNumber(studentId)
              ? `✓ বৈধ পরিচয় (${studentId.length}/17)`
              : `চলমান: ${studentId.length}/17 অঙ্ক`}
          </span>
        </div>
      </header>

      {/* User-friendly Feedback & Error Banners */}
      {errorMsg && (
        <div className="alert alert-error" role="alert">
          <span>⚠️ {errorMsg}</span>
          <button onClick={() => setErrorMsg(null)}>✕</button>
        </div>
      )}

      {successMsg && (
        <div className="alert alert-success" role="status">
          <span>✅ {successMsg}</span>
          <button onClick={() => setSuccessMsg(null)}>✕</button>
        </div>
      )}

      <div className="main-grid">
        {/* Left Column: Admin Class Creation & Live Class List */}
        <div className="column">
          {/* Screen 1: Class তৈরি (School Admin) */}
          <section className="card">
            <h2>১. নতুন Class তৈরি করুন (School Admin)</h2>
            <form onSubmit={handleCreateClass} className="create-class-form">
              <div className="form-group">
                <label htmlFor="class-name-input">Class ও Shift-এর নাম</label>
                <input
                  id="class-name-input"
                  type="text"
                  placeholder="যেমন: Class 6 - Morning Shift"
                  value={newClassName}
                  onChange={(e) => setNewClassName(e.target.value)}
                />
              </div>
              <div className="form-group seats-input">
                <label htmlFor="class-seats-input">মোট আসন (Total Seats)</label>
                <input
                  id="class-seats-input"
                  type="number"
                  min={1}
                  value={newClassSeats}
                  onChange={(e) => setNewClassSeats(e.target.value)}
                />
              </div>
              <button type="submit" className="btn btn-primary" disabled={isCreatingClass}>
                {isCreatingClass ? 'তৈরি হচ্ছে...' : 'Class তৈরি করুন'}
              </button>
            </form>
          </section>

          {/* Screen 2: Class তালিকা */}
          <section className="card">
            <div className="section-header">
              <h2>২. Class তালিকা (Live Seat Status)</h2>
              <span className="live-badge">● স্বয়ংক্রিয় হালনাগাদ</span>
            </div>

            {classes.length === 0 ? (
              <p className="empty-text">এখনো কোনো Class তৈরি করা হয়নি। উপরের ফর্ম থেকে নতুন Class যোগ করুন।</p>
            ) : (
              <div className="table-wrapper">
                <table className="class-table">
                  <thead>
                    <tr>
                      <th>Class-এর নাম</th>
                      <th>মোট (Total)</th>
                      <th>Hold-এ থাকা</th>
                      <th>নিশ্চিত (Confirmed)</th>
                      <th>খালি (Available)</th>
                      <th>পদক্ষেপ</th>
                    </tr>
                  </thead>
                  <tbody>
                    {classes.map((cls) => {
                      const existingActiveForClass = myHolds.find(
                        (h) =>
                          h.classId === cls.id &&
                          (getEffectiveStatus(h) === 'HELD' || getEffectiveStatus(h) === 'CONFIRMED')
                      );
                      const isFull = cls.availableSeats <= 0;
                      const isApplying = busyAction === `apply_${cls.id}`;

                      return (
                        <tr key={cls.id}>
                          <td className="font-semibold">{cls.name}</td>
                          <td>{cls.totalSeats}</td>
                          <td>{cls.heldSeats}</td>
                          <td>{cls.confirmedSeats}</td>
                          <td>
                            <span className={`seat-pill ${cls.availableSeats > 0 ? 'available' : 'full'}`}>
                              {cls.availableSeats}
                            </span>
                          </td>
                          <td>
                            <button
                              className="btn btn-apply"
                              disabled={isFull || Boolean(existingActiveForClass) || isApplying}
                              onClick={() => handleApply(cls)}
                            >
                              {isApplying
                                ? 'অপেক্ষা করুন...'
                                : existingActiveForClass
                                ? existingActiveForClass.status === 'CONFIRMED'
                                  ? 'ভর্তি নিশ্চিত'
                                  : 'আবেদন চলমান'
                                : isFull
                                ? 'আসন পূর্ণ'
                                : 'আবেদন করুন'}
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>

        {/* Right Column: Active Application Detail & My Applications List */}
        <div className="column">
          {/* Screen 3: আবেদন (Selected / Current Hold View with Live Countdown) */}
          <section className="card">
            <h2>৩. আবেদনের বিস্তারিত ও ভর্তি ফি জমা</h2>
            {!isValidBirthRegNumber(studentId) ? (
              <p className="empty-text">
                আপনার চলমান আবেদন দেখতে এবং ভর্তি ফি জমা দিতে উপরে ১৭ অঙ্কের জন্ম নিবন্ধন নম্বর লিখুন।
              </p>
            ) : !activeHold ? (
              <p className="empty-text">
                এই শিক্ষার্থীর কোনো আবেদন নেই। বাম পাশের Class তালিকা থেকে "আবেদন করুন" বাটনে চাপুন।
              </p>
            ) : (
              (() => {
                const effectiveStatus = getEffectiveStatus(activeHold);
                const remainingSecs = getRemainingSeconds(activeHold.expiresAt);
                const isHeldAndValid = effectiveStatus === 'HELD' && remainingSecs > 0;

                return (
                  <div className="active-hold-card">
                    <div className="hold-row">
                      <span>Class:</span>
                      <strong>{activeHold.className || activeHold.classId}</strong>
                    </div>
                    <div className="hold-row">
                      <span>আবেদন আইডি (Hold ID):</span>
                      <code>{activeHold.holdId}</code>
                    </div>
                    <div className="hold-row">
                      <span>বর্তমান অবস্থা (Status):</span>
                      <span className={`status-badge status-${effectiveStatus.toLowerCase()}`}>
                        {effectiveStatus}
                      </span>
                    </div>

                    <div className="hold-row">
                      <span>মেয়াদ শেষ হতে বাকি:</span>
                      {isHeldAndValid ? (
                        <strong className="countdown-timer">{formatCountdown(remainingSecs)}</strong>
                      ) : effectiveStatus === 'CONFIRMED' ? (
                        <span className="text-success">ভর্তি চূড়ান্তভাবে নিশ্চিত হয়েছে</span>
                      ) : effectiveStatus === 'EXPIRED' ? (
                        <span className="text-danger">সময় শেষ (EXPIRED)</span>
                      ) : (
                        <span className="text-muted">বাতিল করা হয়েছে (CANCELLED)</span>
                      )}
                    </div>

                    <div className="hold-actions">
                      <button
                        className="btn btn-confirm"
                        disabled={!isHeldAndValid || busyAction === `confirm_${activeHold.holdId}`}
                        onClick={() => handleConfirmHold(activeHold.holdId)}
                      >
                        {busyAction === `confirm_${activeHold.holdId}` ? 'নিশ্চিত হচ্ছে...' : 'ভর্তি ফি দিন'}
                      </button>

                      <button
                        className="btn btn-cancel"
                        disabled={!isHeldAndValid || busyAction === `cancel_${activeHold.holdId}`}
                        onClick={() => handleCancelHold(activeHold.holdId)}
                      >
                        {busyAction === `cancel_${activeHold.holdId}` ? 'বাতিল হচ্ছে...' : 'বাতিল'}
                      </button>
                    </div>
                  </div>
                );
              })()
            )}
          </section>

          {/* Screen 4: আমার আবেদন (All Applications for this Student) */}
          <section className="card">
            <h2>৪. আমার আবেদন ({myHolds.length})</h2>
            {!isValidBirthRegNumber(studentId) ? (
              <p className="empty-text">১৭ অঙ্কের জন্ম নিবন্ধন নম্বর দিলে এই শিক্ষার্থীর সব আবেদন এখানে দেখা যাবে।</p>
            ) : myHolds.length === 0 ? (
              <p className="empty-text">এখনো কোনো আবেদন করা হয়নি।</p>
            ) : (
              <div className="my-holds-list">
                {myHolds.map((item) => {
                  const effectiveStatus = getEffectiveStatus(item);
                  const remainingSecs = getRemainingSeconds(item.expiresAt);
                  const isSelected = activeHold?.holdId === item.holdId;

                  return (
                    <div
                      key={item.holdId}
                      className={`my-hold-item ${isSelected ? 'selected' : ''}`}
                      onClick={() => selectHold(item.holdId)}
                    >
                      <div className="my-hold-info">
                        <strong>{item.className || item.classId}</strong>
                        <span className="hold-meta">
                          ID: {item.holdId} •{' '}
                          {effectiveStatus === 'HELD' && remainingSecs > 0
                            ? `বাকি: ${formatCountdown(remainingSecs)}`
                            : `সময়সীমা: ${new Date(item.expiresAt).toLocaleTimeString()}`}
                        </span>
                      </div>
                      <div className="my-hold-right">
                        <span className={`status-badge status-${effectiveStatus.toLowerCase()}`}>
                          {effectiveStatus}
                        </span>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}