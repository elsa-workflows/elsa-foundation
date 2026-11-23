// A fixture host is never started. It exists to be *built*: what the tool inspects is its published output
// — a runtimeconfig, a deps file, and whatever the restored package set puts beside them. This one's deps
// file deliberately names no EF provider engine, so the only engine a run can find is the one the host's
// capability selection put in its package set.
return 0;
