export type SchoolClass = {
  id: string;
  name: string;
  grade: string;
  capacity: number;
  availableSeats: number;
};

export type Hold = {
  id: string;
  classId: string;
  className: string;
  studentName: string;
  expiresAt: string;
  status: string;
};

